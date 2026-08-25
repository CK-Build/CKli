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

    [TestCase( 1 )]
    [TestCase( 10 )]
    public async Task parallel_soft_error_Async( int maxDop )
    {
        const int total = 200;

        var handled = new ConcurrentBag<int>();
        var canceled = new ConcurrentBag<int>();
        var pool = new ActivityMonitorAsyncPool( maxDop <= 0 ? int.MaxValue : maxDop );
        // The number maxDop * 3 fails. Others succeed.
        // In SoftStop we don't cancel the ones that have been started: the canceled bag
        // must be empty but we must have more handled than maxDop * 3 but far less than total
        // (for maxDop = 10, we stop at .
        var success = await pool.ParallelAsync( Enumerable.Range( 0, total ),
                                                ( monitor, value, cancellation ) =>
                                                {
                                                    Thread.Sleep( 100 );
                                                    if( cancellation.IsCancellationRequested )
                                                    {
                                                        canceled.Add( value );
                                                        return false;
                                                    }
                                                    bool success = value != maxDop * 3;
                                                    monitor.Info( $"n°{value}: {success}." );
                                                    handled.Add( success ? value : ~value );
                                                    return success;
                                                },
                                                ParallelErrorBehavior.SoftStop,
                                                default );
        success.ShouldBeFalse();
        handled.Count.ShouldBeGreaterThan( maxDop * 3 );
        handled.Count.ShouldBeLessThan( total );
        handled.Single( v => v < 0 ).ShouldBe( ~(maxDop * 3) );
        canceled.ShouldBeEmpty();
    }

    [TestCase( 1 )]
    [TestCase( 10 )]
    public async Task parallel_hard_error_Async( int maxDop )
    {
        const int total = 200;

        var handled = new ConcurrentBag<int>();
        var canceled = new ConcurrentBag<int>();
        var pool = new ActivityMonitorAsyncPool( maxDop <= 0 ? int.MaxValue : maxDop );
        // The number maxDop * 3 fails. Others succeed.
        // In HardStop we cancel the ones that have been started: the canceled bag must NOT be empty
        // and we must have more handled than maxDop * 3 but less than total.
        var success = await pool.ParallelAsync( Enumerable.Range( 0, total ),
                                                ( monitor, value, cancellation ) =>
                                                {
                                                    Thread.Sleep( 200 );
                                                    if( cancellation.IsCancellationRequested )
                                                    {
                                                        canceled.Add( value );
                                                        return false;
                                                    }
                                                    bool success = value != maxDop * 3;
                                                    monitor.Info( $"n°{value}: {success}." );
                                                    handled.Add( success ? value : ~value );
                                                    return success;
                                                },
                                                ParallelErrorBehavior.HardStop,
                                                default );
        success.ShouldBeFalse();
        handled.Count.ShouldBeGreaterThan( maxDop * 3 );
        handled.Count.ShouldBeLessThan( total );
        handled.Single( v => v < 0 ).ShouldBe( ~(maxDop * 3) );
        canceled.ShouldNotBeEmpty();
    }

}
