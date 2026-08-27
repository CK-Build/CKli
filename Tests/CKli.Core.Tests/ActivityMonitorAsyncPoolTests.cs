using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

[TestFixture]
public class ActivityMonitorAsyncPoolTests
{
    [Test]
    public async Task as_resource_limiter_Async()
    {
        var pool = new ActivityMonitorAsyncPool( 5 );
        var monitorTasks = Enumerable.Range( 0, 4 ).Select( i => pool.GetAsync().AsTask() ).ToArray();
        var monitors = await Task.WhenAll( monitorTasks );
        monitors.All( m => m != null ).ShouldBeTrue();

        var cts = new CancellationTokenSource( 100 );

        var lastOne = await pool.GetAsync( cts.Token );
        lastOne.ShouldNotBeNull();

        var notAvailable = await pool.GetAsync( cts.Token );
        notAvailable.ShouldBeNull();

        var condemned = monitors[3].ShouldNotBeNull();
        var condemnedId = condemned.UniqueId;
        condemned.Dispose();
        Should.Throw<ObjectDisposedException>( () => Console.Write( condemned.UniqueId ) );

        cts = new CancellationTokenSource( 100 );
        var available = await pool.GetAsync( cts.Token );
        available.ShouldNotBeNull();

        available.ShouldNotBeSameAs( condemned );
        available.UniqueId.ShouldBe( condemnedId );

    }

    [TestCase( 0 )]
    [TestCase( 1 )]
    [TestCase( 2 )]
    [TestCase( 5 )]
    [TestCase( 10 )]
    public async Task parallel_ignore_error_Async( int maxDop )
    {
        const int total = 200;

        var handled = new ConcurrentBag<int>();
        var pool = new ActivityMonitorAsyncPool( maxDop <= 0 ? int.MaxValue : maxDop );
        var success = await pool.ParallelAsync( Enumerable.Range( 0, total ),
                                                ( monitor, value, cancellation ) =>
                                                {
                                                    bool success = value % 2 == 0;
                                                    monitor.Info( $"n°{value}: {success}." );
                                                    handled.Add( success ? value : ~value );
                                                    return success;
                                                },
                                                ParallelErrorBehavior.Ignore,
                                                default );
        success.ShouldBeFalse();
        handled.Count.ShouldBe( total );
        handled.Distinct().Count().ShouldBe( total );

        int failedCount = handled.Count( i => i < 0 );
        int successCount = handled.Count( i => i >= 0 );
        (failedCount + successCount).ShouldBe( total );
    }

    [TestCase( 2 )]
    [TestCase( 10 )]
    public async Task parallel_soft_error_lets_the_running_tasks_finish_Async( int maxDop )
    {
        const int total = 200;

        // This is what SoftStop does that HardStop doesn't: the actions that are ALREADY running
        // are left alone. They receive the caller's token, not the internal one that ParallelAsync
        // cancels to stop the pending actions. Observing this requires at least 2 actions to run
        // concurrently, hence no maxDop = 1 case here (see parallel_soft_error_stops_pending_tasks_Async).
        //
        // The choreography is ordered by rendezvous, not by delays (no Thread.Sleep, nothing depends
        // on the wall clock):
        //  - The first action entered is the "observer": it captures the token it was given, signals
        //    that it is inside the action, then waits for the failer to have failed - so it is
        //    provably still running when the failure occurs.
        //  - The second one is the "failer": it waits for the observer to be inside the action and
        //    only then returns false.
        //
        // The captured token is asserted AFTER ParallelAsync returned. By then the internal
        // cancellation has necessarily happened: it is what stopped the pending actions, which the
        // last assertion checks. So if SoftStop wrongly handed out that internal token, the captured
        // one would be canceled. No timing is involved in that verdict.
        using var observerIsInside = new ManualResetEventSlim( false );
        using var failerHasFailed = new ManualResetEventSlim( false );
        CancellationToken observerToken = default;
        int arrival = -1;
        var entered = new ConcurrentBag<int>();

        var pool = new ActivityMonitorAsyncPool( maxDop );
        var success = await pool.ParallelAsync( Enumerable.Range( 0, total ),
                                                ( monitor, value, cancellation ) =>
                                                {
                                                    entered.Add( value );
                                                    switch( Interlocked.Increment( ref arrival ) )
                                                    {
                                                        case 0:
                                                            observerToken = cancellation;
                                                            observerIsInside.Set();
                                                            // Deliberately NOT the cancellation token: this wait must not be
                                                            // interruptible, this action must outlive the failure.
                                                            failerHasFailed.Wait( CancellationToken.None );
                                                            monitor.Info( $"n°{value}: observer." );
                                                            return true;
                                                        case 1:
                                                            observerIsInside.Wait( CancellationToken.None );
                                                            monitor.Info( $"n°{value}: soft error." );
                                                            failerHasFailed.Set();
                                                            return false;
                                                        default:
                                                            monitor.Info( $"n°{value}: true." );
                                                            return true;
                                                    }
                                                },
                                                ParallelErrorBehavior.SoftStop,
                                                default );
        success.ShouldBeFalse();
        observerToken.IsCancellationRequested.ShouldBeFalse( "SoftStop must not cancel the actions that are already running." );
        entered.Count.ShouldBeLessThan( total, "The pending actions must not have been started." );
    }

    [TestCase( 1 )]
    [TestCase( 2 )]
    [TestCase( 10 )]
    public async Task parallel_soft_error_stops_pending_tasks_Async( int maxDop )
    {
        const int total = 200;

        // Same as parallel_hard_error_stops_pending_tasks_Async: not starting the pending actions is
        // the behavior SoftStop and HardStop share. Here too, a few actions may already be racing for
        // a monitor when the cancellation occurs: the guarantee is that the enumeration stops early,
        // not that a precise number of actions ran.
        int arrival = -1;
        var entered = new ConcurrentBag<int>();

        var pool = new ActivityMonitorAsyncPool( maxDop );
        var success = await pool.ParallelAsync( Enumerable.Range( 0, total ),
                                                ( monitor, value, cancellation ) =>
                                                {
                                                    entered.Add( value );
                                                    bool success = Interlocked.Increment( ref arrival ) != 0;
                                                    monitor.Info( $"n°{value}: {success}." );
                                                    return success;
                                                },
                                                ParallelErrorBehavior.SoftStop,
                                                default );
        success.ShouldBeFalse();
        entered.Count.ShouldBeLessThan( total );
    }

    // Failure guard for the choreographed wait below. On success this wait is released by the
    // cancellation itself, never by this timeout: it only bounds the test if a regression breaks
    // the cancellation propagation (instead of hanging the test run forever).
    const int _cancellationGuardMs = 10_000;

    [TestCase( 2 )]
    [TestCase( 10 )]
    public async Task parallel_hard_error_cancels_the_running_tasks_Async( int maxDop )
    {
        const int total = 200;

        // This is what HardStop does that SoftStop doesn't: the cancellation token handed to the
        // actions that are ALREADY running gets signaled. Observing it requires at least 2 actions
        // to run concurrently, hence no maxDop = 1 case here: with a single monitor no action can
        // ever be running when another one fails (see parallel_hard_error_stops_pending_tasks_Async).
        //
        // The choreography below is ordered by rendezvous, not by delays (no Thread.Sleep, nothing
        // depends on the wall clock on the success path):
        //  - The first action entered is the "observer": it signals that it is inside the action,
        //    then waits for its own cancellation token to be signaled.
        //  - The second one is the "failer": it first waits for the observer to be inside the
        //    action - so the observer is provably running - and only then returns false.
        //  - ParallelAsync must react to that failure by canceling, which is what releases the
        //    observer. If it didn't, the observer would only be released by the guard timeout.
        using var observerIsInside = new ManualResetEventSlim( false );
        bool observerSawCancellation = false;
        int arrival = -1;
        var entered = new ConcurrentBag<int>();

        var pool = new ActivityMonitorAsyncPool( maxDop );
        var success = await pool.ParallelAsync( Enumerable.Range( 0, total ),
                                                ( monitor, value, cancellation ) =>
                                                {
                                                    entered.Add( value );
                                                    switch( Interlocked.Increment( ref arrival ) )
                                                    {
                                                        case 0:
                                                            observerIsInside.Set();
                                                            observerSawCancellation = cancellation.WaitHandle.WaitOne( _cancellationGuardMs );
                                                            monitor.Info( $"n°{value}: observer, canceled: {observerSawCancellation}." );
                                                            return false;
                                                        case 1:
                                                            // Deliberately NOT the cancellation token: this wait must not be
                                                            // interruptible, the failer has to see the observer running.
                                                            observerIsInside.Wait( CancellationToken.None );
                                                            monitor.Info( $"n°{value}: hard error." );
                                                            return false;
                                                        default:
                                                            monitor.Info( $"n°{value}: true." );
                                                            return true;
                                                    }
                                                },
                                                ParallelErrorBehavior.HardStop,
                                                default );
        success.ShouldBeFalse();
        observerSawCancellation.ShouldBeTrue( "HardStop must cancel the actions that are already running." );
        entered.Count.ShouldBeLessThan( total, "The pending actions must not have been started." );
    }

    [TestCase( 1 )]
    [TestCase( 2 )]
    [TestCase( 10 )]
    public async Task parallel_hard_error_stops_pending_tasks_Async( int maxDop )
    {
        const int total = 200;

        // The first action entered fails: whatever the maxDop, the pending actions must not be
        // started. Note that a few actions may already be racing for a monitor when the
        // cancellation occurs: the guarantee is that the enumeration stops early, not that a
        // precise number of actions ran (this is why no exact count is asserted here).
        int arrival = -1;
        var entered = new ConcurrentBag<int>();

        var pool = new ActivityMonitorAsyncPool( maxDop );
        var success = await pool.ParallelAsync( Enumerable.Range( 0, total ),
                                                ( monitor, value, cancellation ) =>
                                                {
                                                    entered.Add( value );
                                                    bool success = Interlocked.Increment( ref arrival ) != 0;
                                                    monitor.Info( $"n°{value}: {success}." );
                                                    return success;
                                                },
                                                ParallelErrorBehavior.HardStop,
                                                default );
        success.ShouldBeFalse();
        entered.Count.ShouldBeLessThan( total );
    }

}
