using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Capture build information for a <see cref="Repo"/>.
    /// </summary>
    public sealed partial class BuildSolution
    {
        readonly Roadmap _roadmap;
        readonly HotGraph.Solution _solution;
        readonly HotGraph.SolutionVersionInfo _versionInfo;
        BuildInfo? _buildInfo;
        HotGraph.SolutionVersionInfo.LastBuiltVersion _lastBuild;
        int _buildNumber;
        PublishableStatus _publishable;

        internal BuildSolution( Roadmap roadmap, HotGraph.Solution solution, HotGraph.SolutionVersionInfo versionInfo )
        {
            _roadmap = roadmap;
            _solution = solution;
            _versionInfo = versionInfo;
        }

        #region Initialize

        /// <summary>
        /// Captures 3 different package mappers.
        /// </summary>
        sealed class PackagesUpdateDetails
        {
            /// <summary>
            /// The reference to [U]pdate from the World itself (these are necessarily packages that are
            /// emitted from upstream repositories).
            /// </summary>
            public PackageMapper? Updates;

            /// <summary>
            /// The references [C]onfigured in the World (<see cref="VersionTagPlugin.GetPackagesConfiguration(IActivityMonitor)"/>).
            /// </summary>
            public PackageMapper? Configuration;

            /// <summary>
            /// The references that are [D]iscrepancies (other repositories use greater versions).
            /// </summary>
            public PackageMapper? Discrepancies;

            public void Add( PackageInstance origin, SVersion to, int mappingIndex )
            {
                var m = mappingIndex switch
                {
                    0 => Updates ??= new PackageMapper(),
                    1 => Configuration ??= new PackageMapper(),
                    _ => Discrepancies ??= new PackageMapper()
                };
                m.Add( origin.PackageId, origin.Version, to );
            }
        }

        internal bool Initialize( IActivityMonitor monitor )
        {
            if( _buildInfo != null ) return true;

            MustBuildReason buildReason = MustBuildReason.None;

            using var _ = monitor.OpenTrace( $"Initializing Roadmap BuildSolution '{_solution}'." );
            // If upstreams are built, always build.
            if( !InitializeUpstreams( monitor,
                                      out BuildSolution[] directRequirements,
                                      out SVersionChange vChange,
                                      out bool mustBuildFromUpstreams ) )
            {
                return false;
            }
            if( mustBuildFromUpstreams )
            {
                buildReason |= MustBuildReason.UpstreamBuild;
            }
            // If some packages must be updated, then we should build.
            // - The alreadyBuiltMapping enables to fix any intra World package references.
            // - The WorldConfiguredMapping applies the VersionTag plugin configuration.
            // - The DiscrepanciesMapping unifies external versions (to the max existing version).
            //
            // The PackagesUpdateDetails collects up to 3 PackageMapper with the package updates for this
            // solution. The display of the roadmap renders them (with the 'U', 'C' and 'D' letters).
            //
            var alreadyBuiltMapping = _roadmap._packageUpdater.GetAlreadyBuiltMapping( _roadmap.IsCIBuild );
            var packageUpdates = new PackagesUpdateDetails();
            if( _solution.GitSolution.HasUpdates( packageUpdates.Add,
                                                  mustBuildFromUpstreams ? null : alreadyBuiltMapping, // U
                                                  _roadmap._packageUpdater.WorldConfiguredMapping,     // C
                                                  _roadmap._packageUpdater.DiscrepanciesMapping ) )    // D
            {
                // Only consider 'C' and 'D' here: 'U' is considered "skippable".
                if( packageUpdates.Configuration != null || packageUpdates.Discrepancies != null )
                {
                    buildReason |= MustBuildReason.DependencyUpdate;
                }
            }
            Throw.DebugAssert( buildReason == MustBuildReason.None || (buildReason & (MustBuildReason.UpstreamBuild | MustBuildReason.DependencyUpdate)) != 0 );

            // If build is not required here, we check the lastBuild version.
            // The last build tag may be a +fake or a +deprecated: we decide to always trigger a build in such cases:
            // ==> These edge cases are not "skippable".

            _lastBuild = _versionInfo.GetLastBuild( _roadmap.IsCIBuild );
            if( _lastBuild.VersionMustBuild )
            {
                Throw.DebugAssert( _lastBuild.TagCommit.IsFakeVersion || _lastBuild.TagCommit.IsDeprecatedVersion );
                buildReason |= _lastBuild.TagCommit.IsFakeVersion
                                ? MustBuildReason.FakeVersion
                                : MustBuildReason.DeprecatedVersion;
            }

            // Still none? Save the "last build failed" case.
            // When the last build consumes packages in the alreadyBuiltMapping in a different version, it means that
            // we are a solution that is impacted by the upstreams but none of our upstreams must be built (not UpstreamBuild) AND our *.csproj are
            // up to date (not CodeChange). This happens when a our upstreams have been built, our *.csproj have been updated but last build failed
            // miserably: the last build tag has not been updated with the upstreams versions.
            if( buildReason == MustBuildReason.None )
            {
                Throw.DebugAssert( _lastBuild.TagCommit.BuildContentInfo != null );
                foreach( var c in _lastBuild.TagCommit.BuildContentInfo.Consumed )
                {
                    if( alreadyBuiltMapping.TryGetMappedVersion( c.PackageId, c.Version, out var mapped ) && c.Version != mapped )
                    {
                        buildReason |= MustBuildReason.UpstreamVersion;
                        monitor.Trace( $"MustBuildReason.UpstreamVersion for '{_solution}' because of (at least) '{c}' -> {mapped}." );
                        break;
                    }
                }
            }

            if( buildReason == MustBuildReason.None )
            {
                // Pivot dependent conditions: this build can be skipped (not in the scope).
                // This is the only place where the "star build" appears!
                bool canSkip = !_roadmap._isPullBuild && _roadmap._graph.HasPivots && !_solution.IsPivot;
                if( !canSkip )
                {
                    UpdateSkippableBuildReason( packageUpdates, _lastBuild, _roadmap._ciBuildMode, ref buildReason );
                }
                if( buildReason == MustBuildReason.None )
                {
                    // 
                    if( packageUpdates.Updates != null )
                    {
                        monitor.Error( $"""
                            '{_solution}' should not be built but requires dependency updates ({packageUpdates.Updates}).
                            This situation should not happen and reflects a bad repository topology or weird manual modifications that should be fixed manually.
                            """ );
                        return false;
                    }
                    // The version target is the last built one.
                    var vTarget = _lastBuild.Version;
                    Throw.DebugAssert( "Fake version triggered MustBuildReason.FakeVersion.", !vTarget.HasFakeMetadata );
                    // If we are in --ci.0 mode and in non skippable conditions and we are here (MustBuildReason.None),
                    // then the version to consider must be the ci.0 version (not the non-CI build version associated to the TagCommit).
                    // This ci.0 version necessarily exists otherwise the UpdateSkippableBuildReason would have returned the "CI0" reason.
                    //
                    // And because in --ci.0 mode, the last build has been obtained with allowCI = true, we have here:
                    //
                    //  _roadmap._ciBuildMode == CIBuildMode.CIForce && !canSkip => vTarget is CI and if ci.0 then it is the TagCommit.CI0Version.
                    //
                    Throw.DebugAssert( !(_roadmap._ciBuildMode == CIBuildMode.CIForce && !canSkip)
                                        || (vTarget.CINumber > 0 || (vTarget.CINumber == 0 && ReferenceEquals( vTarget, _lastBuild.TagCommit.CI0Version ))) );

                    // We compute the version change not for us (this solution will not be built) but for
                    // the downstream solutions to correctly propagate the change level (here it may be None).
                    // If the LastStable is a +fake, we consider no impact (there's no code change if we are here).
                    // IsPreviousVersionNumbersOf doesn't care of +fake version metadata and returns None when the
                    // Major.Minor.Patch are equal: we have nothing special to do.
                    if( !_versionInfo.BaseBuild.Version.IsPreviousVersionNumbersOf( monitor, vTarget, out vChange ) )
                    {
                        return false;
                    }
                    _buildInfo = new BuildInfo( this,
                                                MustBuildReason.None,
                                                null,
                                                vChange,
                                                vTarget,
                                                directRequirements,
                                                packageUpdates.Updates,
                                                packageUpdates.Configuration,
                                                packageUpdates.Discrepancies );
                    Throw.DebugAssert( "A package can only depend on a package produced on the same branch or one of its ancestors (BranchName.HasChild).",
                                        CheckDependencyBranches( monitor, vTarget, directRequirements ) );
                    return true;
                }
            }
            Throw.DebugAssert( "We must build.", buildReason != MustBuildReason.None );
            // Since we must build, let's update the reason with all its reasons for coherency (and its costs nothing).
            UpdateSkippableBuildReason( packageUpdates, _lastBuild, _roadmap._ciBuildMode, ref buildReason );

            // We must now compute the target version. This uses the vChange that may have been set by the upstream and
            // if the upstreams don't force a Major, we compute the vChange from the code in this repository (previous version
            // tags and conventional commit messages if needed).
            //
            // If we are building from the upstreams or the dependencies must be updated, then we need one more
            // commit to update the dependencies.
            bool mustAddCommit = (buildReason & (MustBuildReason.UpstreamBuild | MustBuildReason.DependencyUpdate)) != 0;
            SVersion? targetVersion = _versionInfo.TagCommitTree.ComputeTargetVersion( monitor,
                                                                                       ref vChange,
                                                                                       _roadmap.Graph.BranchName,
                                                                                       _roadmap._ciBuildMode != CIBuildMode.None,
                                                                                       mustAddCommit,
                                                                                       allowLocal: false );
            if( targetVersion == null )
            {
                return false;
            }
            targetVersion = targetVersion.SetParsedPrefix( "building/" );

            // If the base version is a +fake, then IF this happens to be published we must ensure
            // that the +fake tag appears on the remote otherwise the target version will not be "understandable".
            if( _versionInfo.BaseBuild.IsFakeVersion )
            {
                Throw.DebugAssert( "This has been checked by VersionTagPlugin.Create.",
                                    string.IsNullOrEmpty( _versionInfo.BaseBuild.Version.ParsedPrefix ) );
                Repo.GitRepository.DeferredPushRefSpecs.Add( $"+{_versionInfo.BaseBuild.Tag.CanonicalName}" );
            }
            monitor.Info( $"'{_solution}' build reason: '{buildReason}', computed target version: '{targetVersion}'." );

            // The BuildBranch is the "theoretical branch name".
            var buildBranch = _versionInfo.Solution.Branch;
            if( buildBranch.BranchName != _roadmap.Graph.BranchName )
            {
                buildBranch = _versionInfo.Solution.BranchInfo.Branches[_roadmap.Graph.BranchName.Index];
            }
            _buildInfo = new BuildInfo( this,
                                        buildReason,
                                        buildBranch,
                                        vChange,
                                        targetVersion,
                                        directRequirements,
                                        packageUpdates.Updates,
                                        packageUpdates.Configuration,
                                        packageUpdates.Discrepancies );
            Throw.DebugAssert( "A package can only depend on a package produced on the same branch or one of its ancestors (BranchName.HasChild).",
                                CheckDependencyBranches( monitor, targetVersion, directRequirements ) );
            _roadmap._buildSolutionCount++;
            return true;

            static void UpdateSkippableBuildReason( PackagesUpdateDetails packageUpdates,
                                                    HotGraph.SolutionVersionInfo.LastBuiltVersion lastBuild,
                                                    CIBuildMode ciBuildMode,
                                                    ref MustBuildReason buildReason )
            {
                if( lastBuild.HasCodeChange )
                {
                    buildReason |= MustBuildReason.CodeChange;
                }
                if( packageUpdates.Updates != null )
                {
                    buildReason |= MustBuildReason.DependencyUpdate;
                }
                // We don't want the "CI0" to appear if any other reason exists (this is particularly true
                // when any dependency update must be done because a new commit will be created and this will
                // be a "regular" "ci.1" version.
                if( buildReason == MustBuildReason.None
                    && ciBuildMode == CIBuildMode.CIForce
                    && !lastBuild.TagCommit.Version.IsCI
                    && lastBuild.TagCommit.CI0VersionTag == null )
                {
                    buildReason |= MustBuildReason.CI0;
                }
            }
        }

        /// <summary>
        /// Checks that every direct requirement's produced version is on the same branch as
        /// <paramref name="version"/> or on one of its ancestor branches (<see cref="BranchName.HasChild"/>):
        /// a package can never depend on a package produced on a "hotter" (more specific) branch than its own.
        /// This is guaranteed by construction of the Roadmap (see <see cref="MustBuildReason.UpstreamBuild"/>
        /// and <see cref="MustBuildReason.UpstreamVersion"/> propagation), this is a Debug-only safety net.
        /// </summary>
        bool CheckDependencyBranches( IActivityMonitor monitor, SVersion version, BuildSolution[] requirements )
        {
            var ns = _solution.BranchInfo.Namespace;
            var branch = ns.Find( version );
            if( branch == null ) return true;
            bool success = true;
            foreach( var r in requirements )
            {
                var reqVersion = r.BuildInfo!.TargetVersion;
                var reqBranch = ns.Find( reqVersion );
                if( reqBranch != null && reqBranch != branch && !reqBranch.HasChild( branch ) )
                {
                    monitor.Error( $"""
                        Branch invariant violated: '{_solution}' produces version '{version}' on branch '{branch}' but
                        depends on '{r._solution}' version '{reqVersion}' on branch '{reqBranch}', which is neither
                        '{branch}' nor one of its ancestors.
                        """ );
                    success = false;
                }
            }
            return success;
        }

        bool InitializeUpstreams( IActivityMonitor monitor,
                                  out BuildSolution[] directRequirements,
                                  out SVersionChange maxVersionChange,
                                  out bool mustBuild )
        {
            var solutionRequirements = _solution.DirectRequirements;
            maxVersionChange = SVersionChange.None;
            mustBuild = false;
            directRequirements = new BuildSolution[solutionRequirements.Count];
            int idxReq = 0;
            foreach( var req in solutionRequirements )
            {
                var sReq = _roadmap.OrderedSolutions[req.OrderedIndex];
                if( !sReq.Initialize( monitor ) )
                {
                    return false;
                }
                if( sReq.MustBuild )
                {
                    var vReq = sReq.BuildInfo.VersionChange;
                    if( vReq > maxVersionChange )
                    {
                        maxVersionChange = vReq;
                    }
                    mustBuild = true;
                }
                directRequirements[idxReq++] = sReq;
            }
            return true;
        }

#endregion /Initialize

        internal bool ConcludeInitialization( IActivityMonitor monitor,
                                              ArtifactHandlerPlugin artifactHandler,
                                              ref int idxBuildNumber,
                                              ref PublishableStatus publishable )
        {
            Throw.DebugAssert( _publishable is PublishableStatus.None );
            if( _buildInfo != null )
            {
                if( _buildInfo.MustBuild )
                {
                    Throw.DebugAssert( _buildNumber == 0 && idxBuildNumber >= 1 );
                    _buildNumber = idxBuildNumber++;
                    _publishable = PublishableStatus.Build;
                }
                else
                {
                    if( _buildInfo.TargetVersion.IsLocal() )
                    {
                        if( _roadmap._graph.BranchName.Match( _buildInfo.TargetVersion ) )
                        {
                            _publishable = PublishableStatus.PublishRequired;
                        }
                        else
                        {
                            _publishable = PublishableStatus.IndirectPublishRequired;
                        }
                    }
                    else if( _buildInfo.TargetVersion.IsBuilding() )
                    {
                        if( Solution.IsPivotUpstream || !_roadmap._graph.BranchName.Match( _buildInfo.TargetVersion ) )
                        {
                            _publishable = PublishableStatus.BuildingPending;
                        }
                        else
                        {
                            _publishable = PublishableStatus.PublishRequired;
                        }
                    }
                    else 
                    {
                        _publishable = PublishableStatus.AlreadyPublished;
                    }
                }

                if( publishable < _publishable )
                {
                    publishable = _publishable;
                }
            }

            return true;
        }

        /// <summary>
        /// Get the roadmap to which this build belongs.
        /// </summary>
        public Roadmap Roadmap => _roadmap;

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _solution.Repo;

        /// <summary>
        /// Gets the solution.
        /// </summary>
        public HotGraph.Solution Solution => _solution;

        /// <summary>
        /// Gets the <see cref="HotGraph"/> version related information.
        /// </summary>
        public HotGraph.SolutionVersionInfo VersionInfo => _versionInfo;

        /// <summary>
        /// Gets the base version (the <see cref="VersionTagInfo.HotZoneInfo.LastStable"/> version).
        /// </summary>
        public SVersion BaseVersion => _versionInfo.BaseBuild.Version;

        /// <summary>
        /// Gets the <see cref="HotGraph.SolutionVersionInfo.LastBuiltVersion"/>.
        /// </summary>
        public HotGraph.SolutionVersionInfo.LastBuiltVersion LastBuild => _lastBuild;

        /// <summary>
        /// Gets the build info. This is null if this solution is not impacted
        /// by any of the <see cref="Roadmap.Pivots"/>.
        /// </summary>
        public BuildInfo? BuildInfo => _buildInfo;

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        [MemberNotNullWhen( true, nameof( BuildInfo ), nameof( _buildInfo ) )]
        public bool MustBuild => _buildInfo != null && _buildInfo.MustBuild;

        /// <summary>
        /// Gets the 1-based build number in the order of the <see cref="HotGraph.Solution.OrderedIndex"/>.
        /// 0 when <see cref="MustBuild"/> is false.
        /// </summary>
        public int BuildNumber => _buildNumber;

        /// <summary>
        /// Gets the publishable status of this solution.
        /// </summary>
        public PublishableStatus PublishableStatus => _publishable;

        internal IRenderable ToRenderable(  ref BuildIndexAndRankDisplayState head, ref RStats stats )
        {
            IRenderable r = head.MoveNext( _buildNumber > 0, marginRight: 0 );
            if( _roadmap.Graph.HasPivots )
            {
                var prefixStyle = new TextStyle( ConsoleColor.Black, ConsoleColor.DarkYellow );
                r = r.AddRight( PivotPrefix( head.Screen, _solution, prefixStyle, marginLeft: 1 ) );
            }

            var statusAndName = RepoName( head.Screen, Repo, MustBuild, BuildInfo == null );
            r = r.AddRight( statusAndName );

            var currentVersion = _lastBuild.TagCommit.Version;
            if( _buildInfo != null )
            {
                if( _buildInfo.MustBuild )
                {
                    Throw.DebugAssert( _buildInfo.BuildReason != MustBuildReason.None );

                    bool replace = currentVersion.IsBuildingOrLocal() && _roadmap.Graph.BranchName.Match( currentVersion );

                    r = r.AddRight( head.Screen.Text( replace ? $"(v{currentVersion})" : $"v{currentVersion}",
                                                      replace ? ConsoleColor.Blue : ConsoleColor.DarkBlue,
                                                      effect: replace ? TextEffect.Strikethrough : TextEffect.Ignore ),
                                    head.Screen.Text( $"→ ⏚/v{_buildInfo.TargetVersion}", ConsoleColor.Green ).Box( marginLeft: 1, marginRight: 1 ),
                                    _buildInfo.RenderBuildReason( head.Screen, ref stats ) );
                }
                else
                {
                    var sCurrentVersion = currentVersion.IsBuildingOrLocal() ? $"⏚/v{currentVersion}" : $"v{currentVersion}";
                    r = r.AddRight( head.Screen.Text( sCurrentVersion, currentVersion.IsBuildingOrLocal() ? ConsoleColor.Blue : ConsoleColor.DarkBlue ) );
                }
            }
            else
            {
                var sCurrentVersion = currentVersion.IsBuildingOrLocal() ? $"⏚/v{currentVersion}" : $"v{currentVersion}";
                r = r.AddRight( head.Screen.Text( sCurrentVersion, ConsoleColor.DarkBlue ) );
            }
            return r;

            static IRenderable PivotPrefix( ScreenType screen, HotGraph.Solution solution, TextStyle style, int marginLeft )
            {
                if( solution.IsPivot )
                {
                    if( solution.IsPivotDownstream )
                    {
                        if( solution.IsPivotUpstream )
                        {
                            return screen.Text( "→⊙→" ).Box( style, marginLeft: marginLeft );
                        }
                        return screen.Text( "⊙→" ).Box( style, marginLeft: marginLeft, paddingLeft: 1 );
                    }
                    else if( solution.IsPivotUpstream )
                    {
                        return screen.Text( "→⊙" ).Box( style, marginLeft: marginLeft, paddingRight: 1 );
                    }
                    return screen.Text( "⊙" ).Box( style, marginLeft: marginLeft, paddingLeft: 1, paddingRight: 1 );
                }
                else if( solution.IsPivotDownstream )
                {
                    if( solution.IsPivotUpstream )
                    {
                        return screen.Text( "→·→" ).Box( style, marginLeft: marginLeft );
                    }
                    else
                    {
                        return screen.Text( "·→" ).Box( style, marginLeft: marginLeft, paddingLeft: 1 );
                    }
                }
                else if( solution.IsPivotUpstream )
                {
                    return screen.Text( "→·" ).Box( style, marginLeft: marginLeft, paddingRight: 1 );
                }
                return screen.EmptyString.Box( style, marginLeft: marginLeft, marginRight: 3 );
            }

            static IRenderable RepoName( ScreenType screen, Repo repo, bool mustBuild, bool outOfScope )
            {
                var status = repo.GitStatus;
                var style = mustBuild
                                ? new TextStyle( status.IsDirty ? ConsoleColor.Red : ConsoleColor.Green, ConsoleColor.Black )
                                : outOfScope
                                    ? new TextStyle( status.IsDirty ? ConsoleColor.DarkRed : ConsoleColor.DarkGray, ConsoleColor.Black, TextEffect.Strikethrough )
                                    : new TextStyle( status.IsDirty ? ConsoleColor.DarkRed : ConsoleColor.DarkGray, ConsoleColor.Black );
                // First Box.
                IRenderable r = screen.Text( repo.DisplayPath, style ).HyperLink( new Uri( repo.WorkingFolder ) );
                r = status.IsDirty
                            ? r.Box( paddingRight: 1 ).AddLeft( screen.Text( "✱", style.With( TextEffect.Regular ) ).Box( paddingRight: 1 ) )
                            : r.Box( paddingLeft: 2, paddingRight: 1 );
                return r.Box();
            }
        }

        /// <summary>
        /// Overridden to return the solution and current/target versions.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => _buildInfo == null
                                                ? _solution.ToString()
                                                : _buildInfo.MustBuild
                                                    ? $"{_solution} [{_lastBuild.Version} => {_buildInfo.TargetVersion}]"
                                                    : $"{_solution} [no build]";
    }
}
