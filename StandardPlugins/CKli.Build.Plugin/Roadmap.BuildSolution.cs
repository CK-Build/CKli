using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
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
        // Null only during the initialization phase: Initialize sets it on every solution before Roadmap.Create
        // returns. It is exposed by the non nullable BuildInfo property.
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
                    // This solution is skipped and its only pending updates are 'U' ones: its sources reference packages
                    // produced by this World in versions that have been superseded.
                    // This is not an error: since this solution is not built, nothing it produces enters this build and the
                    // build outcome is unaffected. The updates are kept in the BuildInfo below (and rendered) so that the
                    // pending misalignment is visible: building this solution (or using a '*build') will align its sources.
                    if( packageUpdates.Updates != null )
                    {
                        Throw.DebugAssert( "Otherwise UpdateSkippableBuildReason would have turned the 'U' updates into a DependencyUpdate reason.",
                                            canSkip );
                        // PackageMapper.ToString() ends each mapping with a new line: it must come last (trimmed).
                        monitor.Warn( $"""
                            '{_solution}' is skipped but its sources require dependency updates.
                            Nothing it produces enters this build. Build it explicitly (or use a '*build') to align its sources.
                            {packageUpdates.Updates.ToString().TrimEnd()}
                            """ );
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
            // The build reason is not the only thing that makes the executor add a commit: it also adds one
            // when the commit to build already bears a version that cannot produce the target one. That is
            // independent of UpstreamBuild/DependencyUpdate, so the prediction above can miss it - and a
            // missed commit means a CI number one too low (a "--ci.0" that is not on its base commit).
            // RoadmapExecutor decides with this very same predicate, so the two cannot diverge.
            if( !mustAddCommit
                && _versionInfo.VersionTagInfo.RequiresNewCommit( _versionInfo.TagCommitTree.Tip,
                                                                  targetVersion,
                                                                  out var newCommitReason ) )
            {
                monitor.Trace( $"""
                    A new commit will be created for '{_solution}': incrementing the CI number of '{targetVersion}'.
                    {newCommitReason}
                    """ );
                mustAddCommit = true;
                // ComputeTargetVersion only ever ratchets vChange up, so calling it again is idempotent
                // apart from the CI number we want incremented.
                targetVersion = _versionInfo.TagCommitTree.ComputeTargetVersion( monitor,
                                                                                 ref vChange,
                                                                                 _roadmap.Graph.BranchName,
                                                                                 _roadmap._ciBuildMode != CIBuildMode.None,
                                                                                 mustAddCommit,
                                                                                 allowLocal: false );
                if( targetVersion == null )
                {
                    return false;
                }
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
                var reqVersion = r.TargetVersion;
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
            if( MustBuild )
            {
                Throw.DebugAssert( _buildNumber == 0 && idxBuildNumber >= 1 );
                _buildNumber = idxBuildNumber++;
                _publishable = PublishableStatus.Build;
            }
            else
            {
                var target = BuildInfo.TargetVersion;
                if( target.IsLocal() )
                {
                    _publishable = _roadmap._graph.BranchName.Match( target )
                                    ? PublishableStatus.PublishRequired
                                    : PublishableStatus.IndirectPublishRequired;
                }
                else if( target.IsBuilding() )
                {
                    _publishable = Solution.IsPivotUpstream || !_roadmap._graph.BranchName.Match( target )
                                    ? PublishableStatus.BuildingPending
                                    : PublishableStatus.PublishRequired;
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
        /// Gets the build info. Every solution of an initialized roadmap has one.
        /// </summary>
        public BuildInfo BuildInfo => _buildInfo!;

        /// <summary>
        /// Gets the version this solution will offer once this roadmap is done: the <see cref="Roadmap.BuildInfo.TargetVersion"/>,
        /// which is the "building/" version when this solution is built and the branch resolved
        /// <see cref="LastBuild"/> version otherwise (<see cref="HotGraph.SolutionVersionInfo.LastBuiltVersion.BranchName"/>
        /// is the closest branch to the roadmap's one from which the tag is available).
        /// <para>
        /// <see cref="SVersion"/> equality ignores the "building/" prefix, so this compares directly with the versions
        /// recorded by the consumers of this solution's packages.
        /// </para>
        /// </summary>
        public SVersion TargetVersion => BuildInfo.TargetVersion;

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        public bool MustBuild => BuildInfo.MustBuild;

        /// <summary>
        /// Gets whether this solution is not built but its sources reference packages produced by this World in
        /// versions that have been superseded: these <see cref="BuildInfo.UUpdates"/> are left pending by this
        /// roadmap and only a build of this solution can align them.
        /// <para>
        /// This can only be true for a skipped solution: a non-pivot solution of a "build"/"publish" roadmap that
        /// has pivots. A "*build"/"*publish" (or an "--all"/stack root roadmap, where no solution is a pivot) never
        /// skips anything, so such pending updates always become a <see cref="MustBuildReason.DependencyUpdate"/>
        /// there. 'C' and 'D' updates always trigger a build: only 'U' ones can be left pending.
        /// </para>
        /// </summary>
        public bool HasPendingUpdates => !BuildInfo.MustBuild && BuildInfo.UUpdates != null;

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

            var statusAndName = RepoName( head.Screen, Repo, MustBuild );
            r = r.AddRight( statusAndName );

            var currentVersion = _lastBuild.TagCommit.Version;
            if( MustBuild )
            {
                Throw.DebugAssert( BuildInfo.BuildReason != MustBuildReason.None );

                bool replace = currentVersion.IsBuildingOrLocal() && _roadmap.Graph.BranchName.Match( currentVersion );

                r = r.AddRight( head.Screen.Text( replace ? $"(v{currentVersion})" : $"v{currentVersion}",
                                                  replace ? ConsoleColor.Blue : ConsoleColor.DarkBlue,
                                                  effect: replace ? TextEffect.Strikethrough : TextEffect.Ignore ),
                                head.Screen.Text( $"→ ⏚/v{BuildInfo.TargetVersion}", ConsoleColor.Green ).Box( marginLeft: 1, marginRight: 1 ),
                                BuildInfo.RenderBuildReason( head.Screen, ref stats ) );
            }
            else
            {
                var sCurrentVersion = currentVersion.IsBuildingOrLocal() ? $"⏚/v{currentVersion}" : $"v{currentVersion}";
                r = r.AddRight( head.Screen.Text( sCurrentVersion, currentVersion.IsBuildingOrLocal() ? ConsoleColor.Blue : ConsoleColor.DarkBlue ) );
                if( HasPendingUpdates )
                {
                    r = r.AddRight( BuildInfo.RenderPendingUpdates( head.Screen, ref stats ).Box( marginLeft: 1 ) );
                }
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

            static IRenderable RepoName( ScreenType screen, Repo repo, bool mustBuild )
            {
                var status = repo.GitStatus;
                var style = mustBuild
                                ? new TextStyle( status.IsDirty ? ConsoleColor.Red : ConsoleColor.Green, ConsoleColor.Black )
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
        // ToString must remain usable while the solution is being initialized (it is used by the traces of
        // Initialize itself), hence the only test of the _buildInfo field's nullability outside Initialize.
    }
}
