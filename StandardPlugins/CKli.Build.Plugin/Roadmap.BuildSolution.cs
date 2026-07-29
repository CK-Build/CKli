using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
        HotGraph.SolutionVersionInfo.BuiltVersion _lastBuild;
        BuildContentInfo? _lastBuildToPublish;
        int _buildNumber;
        bool _mustPublish;

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
                    var vTarget = _lastBuild.TagCommit.Version;
                    Throw.DebugAssert( "Fake version triggered MustBuildReason.FakeVersion.", !vTarget.HasFakeMetadata );
                    // If we are in --ci.0 mode and considered the non skippable conditions and we are here (MustBuildReason.None),
                    // then the version to consider must be the ci.0 version (not the non-CI build version associated to the TagCommit).
                    // This ci.0 version necessarily exists otherwise the UpdateSkippableBuildReason would have returned the "CI0" reason.
                    if( _roadmap._ciBuildMode == CIBuildMode.CIForce && !canSkip && !vTarget.IsCI )
                    {
                        Throw.DebugAssert( _lastBuild.TagCommit.CI0VersionTag != null );
                        vTarget = vTarget.SetCINumber( 0 );
                        Throw.DebugAssert( _lastBuild.TagCommit.CI0VersionTag.CanonicalName.EndsWith( vTarget.ToString(), StringComparison.Ordinal ) );
                    }
                    // We compute the version change not for us (this solution will not be built) but for
                    // the downstream solutions to correctly propagate the change level (here it may be None).
                    // If the LastStable is a +fake, we consider no impact (there's no code change if we are here).
                    // In practice, commits should appear above the fake LastStable with their conventional commit
                    // messages that can introduce breaking and feature changes.
                    // IsPreviousVersionNumbersOf doesn't care of +fake version metadata and returns None when the
                    // Major.Minor.Patch are equal: we have nothing special to do.
                    if( !_versionInfo.BaseBuild.Version.IsPreviousVersionNumbersOf( monitor, vTarget, out vChange ) )
                    {
                        return false;
                    }
                    _buildInfo = new BuildInfo( this,
                                                MustBuildReason.None,
                                                vChange,
                                                vTarget,
                                                directRequirements,
                                                packageUpdates.Updates,
                                                packageUpdates.Configuration,
                                                packageUpdates.Discrepancies );
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
            targetVersion = targetVersion.SetParsedPrefix( "local/" );

            // If the base version is a +fake, then IF this happens to be published we must ensure
            // that the +fake tag appears on the remote otherwise the target version will not be "understandable".
            if( _versionInfo.BaseBuild.IsFakeVersion )
            {
                Throw.DebugAssert( "This has been checked by VersionTagPlugin.Create.",
                                    string.IsNullOrEmpty( _versionInfo.BaseBuild.Version.ParsedPrefix ) );
                Repo.GitRepository.DeferredPushRefSpecs.Add( $"+{_versionInfo.BaseBuild.Tag.CanonicalName}" );
            }
            monitor.Info( $"'{_solution}' build reason: '{buildReason}', computed target version: '{targetVersion}'." );
            _buildInfo = new BuildInfo( this,
                                        buildReason,
                                        vChange,
                                        targetVersion,
                                        directRequirements,
                                        packageUpdates.Updates,
                                        packageUpdates.Configuration,
                                        packageUpdates.Discrepancies );
            _roadmap._buildSolutionCount++;
            return true;

            static void UpdateSkippableBuildReason( PackagesUpdateDetails packageUpdates,
                                                    HotGraph.SolutionVersionInfo.BuiltVersion lastBuild,
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
                                              ArtifactHandlerPlugin artifactHandlerPlugin,
                                              ref int idxBuildNumber )
        {
            Throw.DebugAssert( !_mustPublish );
            if( MustBuild )
            {
                Throw.DebugAssert( _buildNumber == 0 && idxBuildNumber >= 1 );
                _buildNumber = idxBuildNumber++;
                _mustPublish = true;
                ++_roadmap._publishSolutionCount;
            }
            else
            {
                // The CurrentVersion may already be published (not "local/" anymore).
                // If the CurrentVersion (that is the last built version) is "local/", we must  publish it (comes from a previous build).
                //
                // But, in order to publish it, its artifacts must be locally available... If not, we must rebuild this version (and eventually
                // publish it).
                // This is an unusual situation as this version should be available somewhere!
                // First idea was to consider that this must be fixed here (and without the "upstream pivot condition"):
                // even if this happens in an upstream of a Pivot, we must trigger the build of this solution.
                // However, this looks more like an issue that can be detected at the VersionTagInfo level, when "ckli issue" is
                // executed (not preemptively), so we error here and ask the user to use "ckli issue". This avoid the "_mustPublish"
                // to appear in the Initialize step and scopes it only here in the ConcludeInitialization step.
                //
                _mustPublish = CurrentVersion.IsLocal();
                if( _mustPublish )
                {
                    _lastBuildToPublish = _lastBuild.TagCommit.BuildContentInfo;
                    Throw.DebugAssert( "Because CurrentVersion cannot be a +fake (MustBuild would be true).", _lastBuildToPublish != null ); 
                    if( !artifactHandlerPlugin.HasAllArtifacts( monitor, _solution.Repo, CurrentVersion, _lastBuildToPublish, out _ ) )
                    {
                        monitor.Error( $"""
                        Repository '{Repo.DisplayPath}' must be published in existing version '{CurrentVersion}' but this version misses local artifacts.
                        Use "maintenance rebuild version" to rebuild it.
                        """ );
                        return false;
                    }
                    ++_roadmap._publishSolutionCount;
                }
                Throw.DebugAssert( _buildNumber == 0 );
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
        /// Gets the version related information.
        /// </summary>
        public HotGraph.SolutionVersionInfo VersionInfo => _versionInfo;

        /// <summary>
        /// Gets the base version (the <see cref="VersionTagInfo.HotZoneInfo.LastPublishedStable"/> version).
        /// </summary>
        public SVersion BaseVersion => _versionInfo.BaseBuild.Version;

        /// <summary>
        /// Gets the current version (from <see cref="HotGraph.SolutionVersionInfo.GetLastBuild(bool)"/>).
        /// </summary>
        public SVersion CurrentVersion => _lastBuild.TagCommit.Version;

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
        /// Gets whether this solution should be published:
        /// <see cref="MustBuild"/> is true (the <see cref="BuildInfo.TargetVersion"/> must be published) or the <see cref="CurrentVersion"/>
        /// is not in the published database.
        /// </summary>
        public bool MustPublish => _mustPublish;

        /// <summary>
        /// Gets the version and content that must be published. This MUST be called only if <see cref="MustPublish"/> is
        /// true and after a successful <see cref="Roadmap.BuildAsync(IActivityMonitor, CKliEnv, BuildPlugin, bool?, int)"/>.
        /// </summary>
        /// <returns>The version and content to publish.</returns>
        public (SVersion Version, Tag Tag, BuildContentInfo Content) GetFinalPublishInfo()
        {
            Throw.CheckState( MustPublish );
            Throw.CheckState( "A successful build must have been done before.", !MustBuild || BuildInfo.BuildResult != null );

            Throw.DebugAssert( MustBuild || _lastBuildToPublish != null );
            return MustBuild
                    ? (BuildInfo.TargetVersion, BuildInfo.BuildResult!.VersionTag, BuildInfo.BuildResult!.Content)
                    : (CurrentVersion, _lastBuild.TagCommit.Tag, _lastBuildToPublish!);
        }

        /// <summary>
        /// Gets the 1-based build number in the order of the <see cref="HotGraph.Solution.OrderedIndex"/>.
        /// 0 when <see cref="MustBuild"/> is false.
        /// </summary>
        public int BuildNumber => _buildNumber;

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
            bool localCurrentVersion = CurrentVersion.IsLocal();
            if( MustBuild )
            {
                Throw.DebugAssert( "An error has been emitted if a MustBuild target version has already been published.", _mustPublish );
                Throw.DebugAssert( BuildInfo.BuildReason != MustBuildReason.None );

                r = r.AddRight( head.Screen.Text( $"v{CurrentVersion}", ConsoleColor.Blue, effect: localCurrentVersion ? TextEffect.Strikethrough : TextEffect.Ignore ),
                                head.Screen.Text( $"→ 🡡/v{BuildInfo.TargetVersion}", ConsoleColor.Green ).Box( marginLeft: 1, marginRight: 1 ),
                                BuildInfo.RenderBuildReason( head.Screen, ref stats ) );
            }
            else
            {
                var currentVersion = localCurrentVersion ? $"🡡/v{CurrentVersion}" : $"v{CurrentVersion}";
                r = r.AddRight( head.Screen.Text( currentVersion, _mustPublish ? ConsoleColor.Blue : ConsoleColor.DarkBlue ) );
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

        [GeneratedRegex( @"^(?<1>\w+)(?:\((?<2>[^()]+)\))?(?<3>!)?:", RegexOptions.CultureInvariant )]
        private static partial Regex ConventionalCommitHeader();

        /// <summary>
        /// Overridden to return the solution and current/target versions.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => _buildInfo == null
                                                ? $"{_solution} [out of scope]"
                                                : _buildInfo.MustBuild
                                                    ? $"{_solution} [{CurrentVersion} => {_buildInfo.TargetVersion}]"
                                                    : $"{_solution} [no build]";
    }
}
