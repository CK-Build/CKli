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
            _cancellation = cancellation;
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
            if( _cancellation.IsCancellationRequested ) return null;
            BuildResult[]? result;
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
                Queue<MonitorRequest>? waitingQueue = null;
                Queue<IActivityMonitor> monitorPool = new Queue<IActivityMonitor>( Math.Min( _maxDoP, 32 ) );
                int monitorCount = 0;
                _ = WaitForTerminationAsync();
                int remainingCount = _roadmap.SolutionBuildCount;
                for(; ; )
                {
                    var msg = await _channel.Reader.ReadAsync();
                    if( msg is BuildResult?[] results )
                    {
                        // There SHOULD never be any pending requests here: all tasks have been completed,
                        // they have released their monitor.
                        Throw.DebugAssert( waitingQueue == null || waitingQueue.Count == 0 );
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
                        if( monitorPool.TryDequeue( out var available ) )
                        {
                            req.SetMonitor( monitor, available );
                        }
                        else if( monitorCount < _maxDoP )
                        {
                            req.SetMonitor( monitor, new ActivityMonitor( $"Build Agent n°{++monitorCount}." ) );
                        }
                        else
                        {
                            Throw.DebugAssert( _roadmap.SolutionBuildCount > _maxDoP );
                            waitingQueue ??= new Queue<MonitorRequest>( _roadmap.SolutionBuildCount - _maxDoP );
                            waitingQueue.Enqueue( req );
                        }
                    }
                    else
                    {
                        --remainingCount;
                        if( req.BuildResult != null )
                        {
                            monitor.Info( ScreenType.CKliScreenTag, $"Build '{req.Build.Solution.Repo.DisplayPath}' succeed." );
                        }
                        else
                        {
                            monitor.Error( req.Message );
                        }
                        if( waitingQueue != null && waitingQueue.TryDequeue( out var waiter ) )
                        {
                            waiter.SetMonitor( monitor, req.Acquired );
                        }
                        else
                        {
                            monitorPool.Enqueue( req.Acquired );
                            Throw.DebugAssert( monitorPool.Count <= _maxDoP );
                        }
                    }
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"Unexpected error during roadmap execution.", ex );
                return null;
            }

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
            string _message;
            BuildResult? _buildResult;

            public MonitorRequest( ChannelWriter<object> writer, Roadmap.BuildInfo build )
            {
                _initialize = new TaskCompletionSource<IActivityMonitor>( TaskCreationOptions.RunContinuationsAsynchronously );
                _writer = writer;
                _build = build;
                _message = $"Building roadmap n°{build.Solution.BuildNumber}/{build.Solution.Roadmap.SolutionBuildCount}: '{build.Solution.Repo.DisplayPath}'.";
                _writer.TryWrite( this );
            }

            public Roadmap.BuildInfo Build => _build;

            public Task<IActivityMonitor> AcquireAsync() => _initialize.Task;

            [MemberNotNullWhen( false, nameof( Acquired ) )]
            public bool MustAcquire => _initialize.Task.Status != TaskStatus.RanToCompletion;

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            public IActivityMonitor? Acquired => _initialize.Task.Status != TaskStatus.RanToCompletion ? null : _initialize.Task.Result;
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits

            public string Message => _message;

            public BuildResult? BuildResult => _buildResult;

            public void SetMonitor( IActivityMonitor monitor, IActivityMonitor available )
            {
                monitor.Info( _message );
                _initialize.SetResult( available );
            }

            public void Release( BuildResult? result )
            {
                _buildResult = result;
                if( result == null )
                {
                    _message = $"Failed to build '{_build.Solution.Repo.DisplayPath}'.";
                }
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
            if( !canAmend
                && build.Solution.VersionInfo.VersionTagInfo.TagCommitsBySha.TryGetValue( git.Repository.Head.Tip.Sha, out var already ) )
            {
                var error = already.CanBearVersion( build.TargetVersion );
                if( error != null )
                {
                    monitor.Info( $"""
                        Creating an empty commit to avoid error:
                        {error}
                        """ );
                    // We create the commit on the checked out branch (can be the regular or the "dev/") and
                    // refresh the buildBranch.
                    if( git.Commit( monitor,
                                    $"Producing 'v{build.TargetVersion}' from unchanged '{already.Version.ParsedText}'.",
                                    CommitBehavior.CreateEmptyCommit ) == CommitResult.Error
                        || !buildBranch.Refresh( monitor ) )
                    {
                        return null;
                    }
                    canAmend = true;
                }
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
