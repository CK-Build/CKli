using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    internal sealed class RoadmapExecutor
    {
        readonly Roadmap _roadmap;
        readonly BuildPlugin _buildPlugin;
        readonly Channel<object> _channel;
        readonly CKliEnv _context;
        readonly int _maxDoP;
        readonly CancellationToken _cancellation;
        readonly bool? _runTest;
        readonly bool _singleBuild;
        // "--focus": the builds of the class 0 (see GetPriorityClass) that have not completed yet. While it is
        // not 0, no build of another class starts. Always 0 when the roadmap is not a focused one.
        int _pendingPriorityBuilds;
        // Linked to the caller's cancellation. "--focus" cancels it on the first failure: no new build starts,
        // and a running one stops at its next step. _stopped is true when this happened.
        readonly CancellationTokenSource _stop;
        bool _stopped;
        int _startedCount;

        public RoadmapExecutor( BuildPlugin buildPlugin,
                                CKliEnv context,
                                Roadmap roadmap,
                                bool? runTest,
                                int maxDoP,
                                CancellationToken cancellation )
        {
            Throw.DebugAssert( maxDoP >= 1 );
            Throw.DebugAssert( roadmap.SolutionBuildCount >= 1 );
            _roadmap = roadmap;
            _singleBuild = roadmap.SolutionBuildCount == 1;
            _runTest = runTest;
            _maxDoP = maxDoP;
            _stop = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
            _cancellation = _stop.Token;
            if( roadmap.IsFocus )
            {
                _pendingPriorityBuilds = roadmap.OrderedSolutions.Count( s => s.MustBuild && GetPriorityClass( s.BuildInfo ) == 0 );
            }
            _buildPlugin = buildPlugin;
            _context = context;
            _channel = Channel.CreateUnbounded<object>( new UnboundedChannelOptions() { SingleReader = true } );
        }

        /// <summary>
        /// Entry point of the build: routes between single build by directly calling <see cref="DoBuildAsync"/>
        /// or parallel build with <see cref="RunLoopAsync"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The array of build result on success or null on error.</returns>
        internal async Task<BuildResult[]?> BuildAsync( IActivityMonitor monitor )
        {
            Throw.DebugAssert( _roadmap.SolutionBuildCount > 0 );
            BuildResult[]? result;
            try
            {
                if( _cancellation.IsCancellationRequested ) return null;
                if( _singleBuild )
                {
                    var s = _roadmap.OrderedSolutions.Single( s => s.MustBuild );
                    Throw.DebugAssert( !s.BuildInfo.DirectRequirements.Any( s => s.MustBuild ) );
                    var r = await DoBuildAsync( monitor, s.BuildInfo );
                    result = r != null
                                ? s.BuildInfo.SetSingleBuildResult( r )
                                : null;
                }
                else
                {
                    using( monitor.OpenInfo( $"Building {_roadmap.SolutionBuildCount} solutions (--max-dop {_maxDoP})." ) )
                    {
                        result = await RunLoopAsync( monitor );
                    }
                    if( _stopped )
                    {
                        Throw.DebugAssert( result == null );
                        monitor.Info( ScreenType.CKliScreenTag,
                                      $"Stopped after the first failure ('--focus'): {_roadmap.SolutionBuildCount - _startedCount} of {_roadmap.SolutionBuildCount} builds were not started." );
                    }
                }
            }
            finally
            {
                _stop.Dispose();
            }
            if( result != null )
            {
                using( monitor.OpenInfo( $"Build succeed: committing 'building/' versions to 'local/' ones." ) )
                {
                    try
                    {
                        foreach( var s in _roadmap.OrderedSolutions )
                        {
                            s.BuildInfo.CommitBuilding();
                        }
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( "While committing 'building/ versions to 'local/' ones.", ex );
                        return null;
                    }
                }
            }
            return result;
        }

        async Task<BuildResult[]?> RunLoopAsync( IActivityMonitor monitor )
        {
            try
            {
                // The requests waiting for a monitor, best first (see GetPriorityClass). The arrival sequence
                // breaks the ties, so that without pivots the order is the one of the requests.
                var waiting = new PriorityQueue<MonitorRequest, (int Class, int Seq)>( _roadmap.SolutionBuildCount );
                int arrivalSeq = 0;
                Queue<IActivityMonitor> monitorPool = new Queue<IActivityMonitor>( Math.Min( _maxDoP, 32 ) );
                int monitorCount = 0;
                // Counts the builds that have started: this is the "n°k" of the "Building roadmap n°k/N" log.
                // The BuildNumber is the roadmap order, and the builds don't start in that order.
                int dispatchCount = 0;
                _ = WaitForTerminationAsync();
                for(; ; )
                {
                    var msg = await _channel.Reader.ReadAsync();
                    // Every message already written is handled before any monitor is given: a monitor must go
                    // to the best waiting request, not to the first one that happened to be read. At start, this
                    // is what puts all the builds that are ready in competition.
                    do
                    {
                        if( msg is BuildResult?[] results )
                        {
                            // There SHOULD never be any pending requests here: all tasks have been completed,
                            // they have released their monitor.
                            Throw.DebugAssert( waiting.Count == 0 );
                            while( monitorPool.TryDequeue( out var m ) )
                            {
                                m.MonitorEnd();
                            }
                            return (results.All( r => r != null ) ? results : null)!;
                        }
                        Throw.DebugAssert( msg is MonitorRequest );
                        var req = (MonitorRequest)msg;
                        if( req.MustAcquire )
                        {
                            waiting.Enqueue( req, (GetPriorityClass( req.Build ), arrivalSeq++) );
                        }
                        else
                        {
                            // On a null BuildResult, the error message must have been emitted by the build itself (rather than a generic message here).
                            if( req.BuildResult != null )
                            {
                                monitor.Info( ScreenType.CKliScreenTag, $"Build '{req.Build.Solution.Repo.DisplayPath}' succeed." );
                            }
                            else if( _roadmap.IsFocus && !_stop.IsCancellationRequested )
                            {
                                // The first failure (a null result on an external cancellation is not one).
                                _stopped = true;
                                _stop.Cancel();
                            }
                            if( _pendingPriorityBuilds > 0 && GetPriorityClass( req.Build ) == 0 )
                            {
                                --_pendingPriorityBuilds;
                            }
                            monitorPool.Enqueue( req.Acquired );
                            Throw.DebugAssert( monitorPool.Count <= _maxDoP );
                        }
                    }
                    while( _channel.Reader.TryRead( out msg ) );

                    while( waiting.TryPeek( out var next, out var priority ) )
                    {
                        // The "--focus" barrier: the class 0 is closed upward, so it never waits for the builds
                        // held here. Once stopped, the held builds are released: they end immediately.
                        if( priority.Class > 0 && _pendingPriorityBuilds > 0 && !_stopped ) break;
                        if( !monitorPool.TryDequeue( out var available ) )
                        {
                            if( monitorCount == _maxDoP ) break;
                            available = new ActivityMonitor( $"Build Agent n°{++monitorCount}." );
                        }
                        waiting.Dequeue();
                        // Once stopped, a request still needs its monitor to complete, but it will not build.
                        next.SetMonitor( monitor, available, _stopped ? 0 : ++dispatchCount );
                    }
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"Unexpected error during roadmap execution.", ex );
                return null;
            }

        }

        /// <summary>
        /// The pivots and their upstreams come first, then the pivots' downstreams, then the others: the
        /// developer gets the result of the pivots as soon as their dependencies allow it.
        /// <para>
        /// The class 0 is closed upward (an upstream of an upstream is an upstream), so it never waits for a build
        /// of another class. This is a scheduling order only: the <see cref="Roadmap.OrderedSolutions"/> stays the
        /// topological order. Without pivots, the 3 flags are false and every build is in the class 0.
        /// </para>
        /// </summary>
        static int GetPriorityClass( Roadmap.BuildInfo build )
        {
            var s = build.Solution.Solution;
            return s.IsPivot || s.IsPivotUpstream
                    ? 0
                    : s.IsPivotDownstream ? 1 : 2;
        }

        async Task WaitForTerminationAsync()
        {
            var buildTasks = new Task<bool>[_roadmap.SolutionBuildCount];
            BuildResult?[] req = await Task.WhenAll( _roadmap.OrderedSolutions.Where( s => s.MustBuild )
                                                                       .Select( s => s.BuildInfo.BuildAsync( this ) )
                                                                       .ToArray() );
            _channel.Writer.TryWrite( req );
        }

        sealed class MonitorRequest
        {
            readonly TaskCompletionSource<IActivityMonitor> _initialize;
            readonly ChannelWriter<object> _writer;
            readonly Roadmap.BuildInfo _build;
            BuildResult? _buildResult;

            public MonitorRequest( ChannelWriter<object> writer, Roadmap.BuildInfo build )
            {
                _initialize = new TaskCompletionSource<IActivityMonitor>( TaskCreationOptions.RunContinuationsAsynchronously );
                _writer = writer;
                _build = build;
                _writer.TryWrite( this );
            }

            public Roadmap.BuildInfo Build => _build;

            public Task<IActivityMonitor> AcquireAsync() => _initialize.Task;

            [MemberNotNullWhen( false, nameof( Acquired ) )]
            public bool MustAcquire => _initialize.Task.Status != TaskStatus.RanToCompletion;

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            public IActivityMonitor? Acquired => _initialize.Task.Status != TaskStatus.RanToCompletion ? null : _initialize.Task.Result;
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

            public BuildResult? BuildResult => _buildResult;

            public void SetMonitor( IActivityMonitor monitor, IActivityMonitor available, int dispatchNumber )
            {
                if( dispatchNumber > 0 )
                {
                    monitor.Info( $"Building roadmap n°{dispatchNumber}/{_build.Solution.Roadmap.SolutionBuildCount}: '{_build.Solution.Repo.DisplayPath}'." );
                }
                _initialize.SetResult( available );
            }

            public void Release( BuildResult? result )
            {
                _buildResult = result;
                _writer.TryWrite( this );
            }
        }

        internal async Task<BuildResult?> ParallelBuildAsync( Roadmap.BuildInfo buildInfo )
        {
            Throw.DebugAssert( !_singleBuild );
            // Acquires a monitor.
            var request = new MonitorRequest( _channel.Writer, buildInfo );
            var monitor = await request.AcquireAsync();

            // Actual build.
            BuildResult? result = null;
            try
            {
                result = await DoBuildAsync( monitor, buildInfo );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Error while building '{buildInfo.Solution}'.", ex );
            }
            // Returning the monitor to the pool (and handling centralized success/failure of builds).
            request.Release( result );
            return result;
        }

        // This doesn't catch exception. When called with a true _singleBuild, this is a unhandled
        // command exception handled at the root level.
        // When called in parallel, it is the ParallelBuildAsync wrapper above that handles it.
        async Task<BuildResult?> DoBuildAsync( IActivityMonitor monitor, Roadmap.BuildInfo build )
        {
            if( _cancellation.IsCancellationRequested ) return null;
            Interlocked.Increment( ref _startedCount );

            Throw.DebugAssert( build.MustBuild );
            var repo = build.Solution.Repo;
            var git = repo.GitRepository;

            // EnsureAndCheckoutBranch and UpdateDependenciesAndCommit only interact with their own Repo:
            // parallel builds don't need synchronization for these.

            // If the real branch from which the build must be done doesn't exist, we create and synchronize it.
            var buildBranch = build.BuildBranch;
            if( !buildBranch.Exists )
            {
                if( !buildBranch.EnsureExists( monitor ) || !buildBranch.Synchronize( monitor ) )
                {
                    return null;
                }
            }
            // This checks out the "dev/" (CI build) or integrate it in the regular branch and checks out the regular branch (non CI build).
            if( !EnsureAndCheckoutBranch( monitor, build, buildBranch, out var canAmend ) )
            {
                return null;
            }

            if( _cancellation.IsCancellationRequested ) return null;

            // From now on, we work on the working folder. 

            // If no new commit has been created (canAmend is false), then we check whether the new version
            // requires an independent commit.
            // Roadmap.BuildSolution used this very same predicate to decide the target version's CI number:
            // it MUST answer the same here or the version tag will not land on the commit it claims.
            if( !canAmend
                && build.Solution.VersionInfo.VersionTagInfo.RequiresNewCommit( git.Repository.Head.Tip,
                                                                                build.TargetVersion,
                                                                                out var error ) )
            {
                monitor.Info( $"""
                    Creating an empty commit to avoid error:
                    {error}
                    """ );
                // We create the commit on the checked out branch (can be the regular or the "dev/") and
                // refresh the buildBranch.
                if( git.Commit( monitor,
                                $"Producing 'v{build.TargetVersion}' from unchanged head.",
                                CommitBehavior.CreateEmptyCommit ) == CommitResult.Error
                    || !buildBranch.Refresh( monitor ) )
                {
                    return null;
                }
                canAmend = true;
            }
            // Since we work on the working folder, we must refresh the build branch.
            var commit = UpdateDependenciesAndCommit( monitor, build, _roadmap.PackageMapping, canAmend );
            if( commit == null || !buildBranch.Refresh( monitor ) )
            {
                return null;
            }
            if( _cancellation.IsCancellationRequested ) return null;

            // CoreBuildAsync interacts with the ArtifactHandlerPlugin that is mainly a proxy of the file system (the $Local NuGet and Assets folders).
            var result = await _buildPlugin.CoreBuildAsync( monitor,
                                                            _context,
                                                            build.Solution.VersionInfo.VersionTagInfo,
                                                            commit,
                                                            build.TargetVersion,
                                                            _runTest,
                                                            forceRebuild: !build.TargetVersion.IsCI,
                                                            _cancellation ).ConfigureAwait( false );
            Throw.DebugAssert( "If build succeeded, the produced packages in the last built version must all be mapped to the new target version.",
                               result == null
                               || result.Content.Produced.All( p => _roadmap.PackageMapping.GetMappedVersion( p, build.Solution.LastBuild.Version ) == result.Version ) );
            // On error, we ensure that we let the repository on the "dev/" branch (this applies to non CI
            // build - in CI build we already are on the "dev/" branch).
            if( result == null && !_roadmap.IsCIBuild )
            {
                // The files are exactly the same by design (hard reset has already been done by CoreBuild, no need to handle untracked & ignored files).
                repo.GitRepository.Checkout( monitor, buildBranch.EnsureDevBranch(), deleteUntracked: false );
            }
            return result;

            static bool EnsureAndCheckoutBranch( IActivityMonitor monitor,
                                                 Roadmap.BuildInfo build,
                                                 HotBranch buildBranch,
                                                 out bool canAmend )
            {
                Throw.DebugAssert( buildBranch.Exists && build.Solution.Repo == buildBranch.Repo );
                var gitRepository = build.Solution.Repo.GitRepository;
                Branch workingBranch;
                canAmend = false;

                bool hasDev = buildBranch.GitDevBranch != null;
                if( build.Solution.Roadmap.IsCIBuild )
                {
                    // CI build: easy, always work on the "dev/" branch.
                    workingBranch = buildBranch.EnsureDevBranch();
                }
                else
                {
                    // Stable build: if a "dev/" branch exists, integrate it.
                    if( buildBranch.GitDevBranch != null )
                    {
                        // Allow amend to update dependencies only if a merge commit
                        // has been created.
                        var before = buildBranch.GitDevBranch.Tip.Sha;
                        if( !buildBranch.IntegrateDevBranch( monitor ) )
                        {
                            return false;
                        }
                        // canAmend == a new merge commit has been created. 
                        canAmend = buildBranch.GitBranch.Tip.Sha != before;
                    }
                    workingBranch = buildBranch.GitBranch;
                }
                if( !gitRepository.Checkout( monitor, workingBranch ) )
                {
                    return false;
                }
                return true;
            }

            static Commit? UpdateDependenciesAndCommit( IActivityMonitor monitor,
                                                        Roadmap.BuildInfo build,
                                                        IPackageMapping mapping,
                                                        bool canAmend )
            {
                var repo = build.Solution.Repo;
                var s = MutableSolution.Create( monitor, repo );
                if( s == null )
                {
                    return null;
                }
                var mapped = new PackageMapper();
                if( !s.UpdatePackages( monitor, mapping, mapped ) )
                {
                    return null;
                }
                // Creates the commit... or not: this does nothing if there's nothing to do.
                // Here we create a commit on the regular branch if it has been integrated (non
                // CI build case).
                var commitMsg = $"""
                Updated dependencies.

                {mapped}
                """;

                GitRepository git = repo.GitRepository;
                if( git.Commit( monitor,
                                commitMsg,
                                canAmend
                                  ? CommitBehavior.AmendIfPossibleAndPrependPreviousMessage
                                  : CommitBehavior.CreateNewCommit ) == CommitResult.Error )
                {
                    return null;
                }

                return git.Repository.Head.Tip;
            }
        }
    }

}
