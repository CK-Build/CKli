using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Detailed build related status associated of a <see cref="BuildSolution"/> in a <see cref="Roadmap"/>.
    /// <para>
    /// Available on <see cref="BuildSolution.BuildInfo"/> when the solution belongs to the pivots scope and is
    /// not ignored. 
    /// </para>
    /// <para>
    /// After a successful build and if <see cref="MustBuild"/> is true, then the non null <see cref="BuildResult"/> is available.
    /// </para>
    /// </summary>
    public sealed class BuildInfo
    {
        readonly BuildSolution _solution;
        readonly MustBuildReason _buildReason;
        readonly SVersionChange _versionChange;
        readonly SVersion _targetVersion;
        readonly PackageMapper? _uUpdates;
        readonly PackageMapper? _cUpdates;
        readonly PackageMapper? _dUpdates;
        readonly ImmutableArray<BuildSolution> _directRequirements;

        readonly Lock _buildTaskLock;
        Task<BuildResult?>? _buildTask;
        BuildResult? _buildResult;

        internal BuildInfo( BuildSolution solution,
                            MustBuildReason buildReason,
                            SVersionChange versionChange,
                            SVersion targetVersion,
                            BuildSolution[]? directRequirements,
                            PackageMapper? uUpdates,
                            PackageMapper? cUpdates,
                            PackageMapper? dUpdates )
        {
            _solution = solution;
            _buildReason = buildReason;
            _versionChange = versionChange;
            _targetVersion = targetVersion;
            _uUpdates = uUpdates;
            _cUpdates = cUpdates;
            _dUpdates = dUpdates;
            _directRequirements = directRequirements != null
                                    ? ImmutableCollectionsMarshal.AsImmutableArray( directRequirements )
                                    : [];
            _buildTaskLock = new Lock();

            Throw.DebugAssert( "When we must build then version changes at least Patch.",
                               buildReason == MustBuildReason.None || versionChange >= SVersionChange.Patch );

            Throw.DebugAssert( "Currently the version can never be a +fake (the +fake is not skippable).", !_targetVersion.HasFakeMetadata );

            Throw.DebugAssert( "Any dependency updates appear in the BuildReason.",
                                (uUpdates != null || cUpdates != null || dUpdates != null) == ((_buildReason & MustBuildReason.DependencyUpdate) != 0) );
        }

        public BuildSolution Solution => _solution;

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        public bool MustBuild => _buildReason != MustBuildReason.None;

        /// <summary>
        /// Gets why this solution must be built.
        /// </summary>
        public MustBuildReason BuildReason => _buildReason;

        /// <summary>
        /// Gets the version change level.
        /// </summary>
        public SVersionChange VersionChange => _versionChange;

        /// <summary>
        /// Gets the version that must be produced.
        /// When <see cref="MustBuild"/> is false, this is the last built version (see <see cref="CKli.BranchModel.Plugin.HotGraph.SolutionVersionInfo.LastBuildInCI"/>).
        /// </summary>
        public SVersion TargetVersion => _targetVersion;

        /// <summary>
        /// Gets the other <see cref="BuildSolution"/> that must be built before this one.
        /// </summary>
        public ImmutableArray<BuildSolution> DirectRequirements => _directRequirements;

        /// <summary>
        /// Gets the intra World reference package updates if any.
        /// </summary>
        public PackageMapper? UUpdates => _uUpdates;

        /// <summary>
        /// Gets the package updates from <see cref="HotGraph.PackageUpdater.WorldConfiguredMapping"/> if any.
        /// </summary>
        public PackageMapper? CUpdates => _cUpdates;

        /// <summary>
        /// Gets the package updates from <see cref="HotGraph.PackageUpdater.WorldConfiguredMapping"/> if any.
        /// </summary>
        public PackageMapper? DUpdates => _dUpdates;

        /// <summary>
        /// Gets the build result. Not null when <see cref="MustBuild"/> is true and build succeeded.
        /// </summary>
        public BuildResult? BuildResult => _buildResult;

        internal BuildResult[]? SetSingleBuildResult( BuildResult r )
        {
            _buildResult = r;
            return [r];
        }

        internal Task<BuildResult?> BuildAsync( BuildPlugin.RoadmapExecutor builder )
        {
            if( _buildTask != null ) return _buildTask;
            lock( _buildTaskLock )
            {
                return _buildTask ??= DoBuildAsync( builder );
            }
        }

        async Task<BuildResult?> DoBuildAsync( BuildPlugin.RoadmapExecutor builder )
        {
            Throw.DebugAssert( MustBuild );
            // Wait for requirements.
            if( _directRequirements.Length > 0 )
            {
                // Checks that all required builds went fine (or return null).
                var all = _directRequirements.Where( s => s.MustBuild ).Select( s => s.BuildInfo!.BuildAsync( builder ) ).ToArray();
                BuildResult?[] req = await Task.WhenAll( all ).ConfigureAwait( false );
                foreach( var r in req )
                {
                    if( r == null ) return null;
                }
            }
            // Building requirements succeed: running this build.
            _buildResult = await builder.BuildAsync( this ).ConfigureAwait( false );
            return _buildResult;
        }


        internal IRenderable RenderBuildReason( ScreenType screen, ref RStats stats )
        {
            IRenderable r = screen.Text( $"({_buildReason})", TextStyle.Default.With( TextEffect.Italic ) );
            if( _uUpdates != null )
            {
                // This is used only when building the upstreams is skipped, the updates here are existing upstreams
                // so we use 'U'.
                r = r.AddBelow( stats.GetUDepHead( screen ).AddRight( screen.Text( _uUpdates.ToString(), ConsoleColor.DarkGray ) ) );
                stats.UDepUpdates += _uUpdates.Count;
            }
            if( _cUpdates != null )
            {
                r = r.AddBelow( stats.GetCDepHead( screen ).AddRight( screen.Text( _cUpdates.ToString(), ConsoleColor.DarkGray ) ) );
                stats.CDepUpdates += _cUpdates.Count;
            }
            if( _dUpdates != null )
            {
                r = r.AddBelow( stats.GetDDepHead( screen ).AddRight( screen.Text( _dUpdates.ToString(), ConsoleColor.DarkGray ) ) );
                stats.DDepUpdates += _dUpdates.Count;
            }
            return r;
        }

    }

}

