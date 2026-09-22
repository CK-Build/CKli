using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using AcquireResult = CKli.Core.GitRepository.DistributedLock.AcquireResult;

namespace CKli.Core.Tests;

/// <summary>
/// The distributed lock is a Git reference of the Stack remote whose successive leases are a chain of
/// commits: every update is a fast-forward, which is the only conditional update the Git protocol offers.
/// <para>
/// Everything here runs two clones of one Stack against a local bare remote - the two developers the lock
/// exists to separate.
/// </para>
/// </summary>
[TestFixture]
public class DistributedLockTests
{
    // Every test pushes lock references to the "file://" remote.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [TearDown]
    public void ResetTestHooks()
    {
        GitRepository.DistributedLock.BeforePushHook = null;
        GitRepository.DistributedLock.ClockSkewAllowance = TimeSpan.FromSeconds( 30 );
    }

    static readonly TimeSpan OneMinute = TimeSpan.FromMinutes( 1 );

    [Test]
    public async Task acquire_creates_the_reference_and_release_removes_it_Async()
    {
        using var one = await ArrangeOneAsync();

        one.Stack.GetLock( TestHelper.Monitor, "publish", out var theLock ).ShouldBeTrue();
        theLock.LockReference.ShouldBe( "refs/ckli-locks/publish" );
        RemoteLockRefs( one ).ShouldBeEmpty();

        theLock.TryAcquire( TestHelper.Monitor, OneMinute, out var lease, out var holder )
               .ShouldBe( AcquireResult.Acquired );
        lease.ShouldNotBeNull();
        holder.ShouldBeNull();
        lease.IsExpired.ShouldBeFalse();
        lease.IsReleased.ShouldBeFalse();
        RemoteLockRefs( one ).ShouldBe( new[] { "refs/ckli-locks/publish" } );

        lease.Release( TestHelper.Monitor ).ShouldBeTrue();
        lease.IsReleased.ShouldBeTrue();
        RemoteLockRefs( one ).ShouldBeEmpty( "Releasing deletes the reference: that is what keeps the chain short." );

        // Releasing is idempotent.
        lease.Release( TestHelper.Monitor ).ShouldBeTrue();
    }

    [Test]
    public async Task a_held_lock_is_reported_with_its_holder_Async()
    {
        var (a, b) = await ArrangeTwoAsync();
        using var disposeA = a;
        using var disposeB = b;

        a.Stack.GetLock( TestHelper.Monitor, "publish", out var aLock ).ShouldBeTrue();
        b.Stack.GetLock( TestHelper.Monitor, "publish", out var bLock ).ShouldBeTrue();

        aLock.TryAcquire( TestHelper.Monitor, OneMinute, out var aLease, out _ ).ShouldBe( AcquireResult.Acquired );

        bLock.TryAcquire( TestHelper.Monitor, OneMinute, out var bLease, out var holder )
             .ShouldBe( AcquireResult.Held );
        bLease.ShouldBeNull();
        holder.ShouldNotBeNull();
        holder.OwnerId.ShouldBe( aLock.OwnerId, "The other developer's lease says who is blocking." );
        holder.ExpiresAt.ShouldBe( aLease.ShouldNotBeNull().ExpiresAt );

        // Once released, the second client gets it.
        aLease.Release( TestHelper.Monitor ).ShouldBeTrue();
        bLock.TryAcquire( TestHelper.Monitor, OneMinute, out bLease, out _ ).ShouldBe( AcquireResult.Acquired );
        bLease.ShouldNotBeNull().Release( TestHelper.Monitor ).ShouldBeTrue();
    }

    /// <summary>
    /// The race this mutex exists to close: both clients read a free lock, both build a lease, one pushes
    /// first. The loser must be told "held", not "error" - it has simply to wait.
    /// </summary>
    [Test]
    public async Task losing_the_race_is_Held_and_not_an_error_Async()
    {
        var (a, b) = await ArrangeTwoAsync();
        using var disposeA = a;
        using var disposeB = b;

        a.Stack.GetLock( TestHelper.Monitor, "publish", out var aLock ).ShouldBeTrue();
        b.Stack.GetLock( TestHelper.Monitor, "publish", out var bLock ).ShouldBeTrue();

        GitRepository.DistributedLock.Lease? bLease = null;
        GitRepository.DistributedLock.BeforePushHook = () =>
        {
            // Only once: B's own acquisition pushes too.
            GitRepository.DistributedLock.BeforePushHook = null;
            bLock.TryAcquire( TestHelper.Monitor, OneMinute, out bLease, out _ ).ShouldBe( AcquireResult.Acquired );
        };

        // A read a free lock, then B acquired it before A could push.
        aLock.TryAcquire( TestHelper.Monitor, OneMinute, out var aLease, out _ ).ShouldBe( AcquireResult.Held );
        aLease.ShouldBeNull();

        bLease.ShouldNotBeNull();
        // The lock really is B's: the loser did not corrupt it.
        aLock.TryAcquire( TestHelper.Monitor, OneMinute, out _, out var holder ).ShouldBe( AcquireResult.Held );
        holder.ShouldNotBeNull().OwnerId.ShouldBe( bLock.OwnerId );
        bLease.Release( TestHelper.Monitor ).ShouldBeTrue();
    }

    /// <summary>
    /// An expired lease is how a crashed client stops blocking everybody. The clock skew allowance is what
    /// keeps a developer whose clock runs fast from stealing a lease that is still alive.
    /// </summary>
    [Test]
    public async Task an_expired_lease_is_stolen_only_after_the_clock_skew_allowance_Async()
    {
        var (a, b) = await ArrangeTwoAsync();
        using var disposeA = a;
        using var disposeB = b;

        a.Stack.GetLock( TestHelper.Monitor, "publish", out var aLock ).ShouldBeTrue();
        b.Stack.GetLock( TestHelper.Monitor, "publish", out var bLock ).ShouldBeTrue();

        GitRepository.DistributedLock.ClockSkewAllowance = TimeSpan.FromSeconds( 2 );
        aLock.TryAcquire( TestHelper.Monitor, TimeSpan.FromSeconds( 1 ), out var aLease, out _ )
             .ShouldBe( AcquireResult.Acquired );

        await Task.Delay( 1200 );
        aLease.ShouldNotBeNull().IsExpired.ShouldBeTrue();
        bLock.TryAcquire( TestHelper.Monitor, OneMinute, out var bLease, out _ )
             .ShouldBe( AcquireResult.Held, "Expired, but not for longer than the clock skew allowance." );
        bLease.ShouldBeNull();

        await Task.Delay( 2000 );
        bLock.TryAcquire( TestHelper.Monitor, OneMinute, out bLease, out _ ).ShouldBe( AcquireResult.Acquired );
        bLease.ShouldNotBeNull();

        // The stolen lease cannot release the lock it lost: it would delete B's.
        aLease.Release( TestHelper.Monitor ).ShouldBeFalse();
        RemoteLockRefs( a ).ShouldBe( new[] { "refs/ckli-locks/publish" } );
        bLease.Release( TestHelper.Monitor ).ShouldBeTrue();
    }

    [Test]
    public async Task renew_extends_the_lease_and_fails_once_the_lock_is_taken_over_Async()
    {
        var (a, b) = await ArrangeTwoAsync();
        using var disposeA = a;
        using var disposeB = b;

        a.Stack.GetLock( TestHelper.Monitor, "publish", out var aLock ).ShouldBeTrue();
        aLock.TryAcquire( TestHelper.Monitor, TimeSpan.FromSeconds( 30 ), out var aLease, out _ )
             .ShouldBe( AcquireResult.Acquired );
        var firstToken = aLease.ShouldNotBeNull().Token;
        var firstExpiration = aLease.ExpiresAt;

        aLease.Renew( TestHelper.Monitor, OneMinute ).ShouldBeTrue();
        aLease.Token.ShouldNotBe( firstToken, "Each lease state is a new commit: the token is the fencing token." );
        aLease.ExpiresAt.ShouldBeGreaterThan( firstExpiration );

        // Another client takes the reference over (only a force push can do that while the lease is alive:
        // this is what a renewal has to survive).
        ForcePushLockRef( b, "refs/ckli-locks/publish", HeadTip( b ) );

        aLease.Renew( TestHelper.Monitor, OneMinute )
              .ShouldBeFalse( "The lease commit is no longer the remote tip: the fast-forward is refused." );
    }

    /// <summary>
    /// A deletion is not a fast-forward, so it is not conditional on our own commit: a lease that lost the
    /// reference must not delete the lock of the client that owns it now.
    /// </summary>
    [Test]
    public async Task release_never_deletes_another_client_lock_Async()
    {
        var (a, b) = await ArrangeTwoAsync();
        using var disposeA = a;
        using var disposeB = b;

        // B takes a real lease on another lock: that gives a valid lease commit to move onto A's reference.
        b.Stack.GetLock( TestHelper.Monitor, "other", out var bOther ).ShouldBeTrue();
        bOther.TryAcquire( TestHelper.Monitor, OneMinute, out var bLease, out _ ).ShouldBe( AcquireResult.Acquired );

        a.Stack.GetLock( TestHelper.Monitor, "publish", out var aLock ).ShouldBeTrue();
        aLock.TryAcquire( TestHelper.Monitor, OneMinute, out var aLease, out _ ).ShouldBe( AcquireResult.Acquired );

        ForcePushLockRef( b, "refs/ckli-locks/publish", bLease.ShouldNotBeNull().Token );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            aLease.ShouldNotBeNull().Release( TestHelper.Monitor ).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "This lease no longer owns it." ) );
        }
        RemoteLockRefs( a ).OrderBy( x => x ).ToArray()
                           .ShouldBe( new[] { "refs/ckli-locks/other", "refs/ckli-locks/publish" },
                                      customMessage: "Both locks are intact." );
        bLease.Release( TestHelper.Monitor ).ShouldBeTrue();
    }

    /// <summary>
    /// Mutual exclusion holds only while every client uses the same prefix: two that disagree would both
    /// acquire and neither would see the other. The "ckli-locks" segment that every lock reference carries
    /// is what makes that visible in one pass over the advertised references.
    /// </summary>
    [Test]
    public async Task a_lock_reference_under_another_prefix_is_refused_Async()
    {
        using var a = await ArrangeOneAsync();

        a.Stack.GetLock( TestHelper.Monitor, "publish", out var theLock ).ShouldBeTrue();
        theLock.TryAcquire( TestHelper.Monitor, OneMinute, out var lease, out _ ).ShouldBe( AcquireResult.Acquired );
        lease.ShouldNotBeNull().Release( TestHelper.Monitor ).ShouldBeTrue();

        // A client configured with "refs/heads/ckli-locks" locked the same Stack.
        ForcePushLockRef( a, "refs/heads/ckli-locks/publish", HeadTip( a ) );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            theLock.TryAcquire( TestHelper.Monitor, OneMinute, out _, out _ )
                   .ShouldBe( AcquireResult.Error, "Not 'Held': the lock guarantees nothing at all here." );
            logs.ShouldContain( l => l.Contains( "Another client is locking this Stack under a different prefix" ) );
        }
    }

    [Test]
    public async Task WaitAcquireAsync_gives_up_on_timeout_Async()
    {
        var (a, b) = await ArrangeTwoAsync();
        using var disposeA = a;
        using var disposeB = b;

        a.Stack.GetLock( TestHelper.Monitor, "publish", out var aLock ).ShouldBeTrue();
        b.Stack.GetLock( TestHelper.Monitor, "publish", out var bLock ).ShouldBeTrue();
        aLock.TryAcquire( TestHelper.Monitor, OneMinute, out var aLease, out _ ).ShouldBe( AcquireResult.Acquired );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var (result, lease) = await bLock.WaitAcquireAsync( TestHelper.Monitor, OneMinute, TimeSpan.FromSeconds( 3 ) );
            result.ShouldBe( AcquireResult.Held );
            lease.ShouldBeNull();
            logs.ShouldContain( l => l.Contains( "while waiting for the lock 'refs/ckli-locks/publish'" ) );
        }
        aLease.ShouldNotBeNull().Release( TestHelper.Monitor ).ShouldBeTrue();

        var (r2, l2) = await bLock.WaitAcquireAsync( TestHelper.Monitor, OneMinute, TimeSpan.FromSeconds( 3 ) );
        r2.ShouldBe( AcquireResult.Acquired );
        l2.ShouldNotBeNull().Release( TestHelper.Monitor ).ShouldBeTrue();
    }

    /// <summary>
    /// A lease is sized on how long a crashed client may block the team, never on how long the work takes: an
    /// operation that outlives it renews at its checkpoints. The half of the lease is the whole margin - before
    /// it, a checkpoint costs nothing at all, which is what lets one be placed wherever the operation offers it.
    /// </summary>
    [Test]
    public async Task KeepAlive_renews_only_once_the_lease_is_half_over_Async()
    {
        using var one = await ArrangeOneAsync();

        one.Stack.GetLock( TestHelper.Monitor, "publish", out var theLock ).ShouldBeTrue();
        theLock.TryAcquire( TestHelper.Monitor, TimeSpan.FromSeconds( 4 ), out var lease, out _ )
               .ShouldBe( AcquireResult.Acquired );
        lease.ShouldNotBeNull();
        lease.Duration.ShouldBe( TimeSpan.FromSeconds( 4 ) );

        var token = lease.Token;
        var expiresAt = lease.ExpiresAt;
        lease.KeepAlive( TestHelper.Monitor ).ShouldBeTrue();
        lease.Token.ShouldBe( token, "Not half over yet: nothing has been pushed." );

        await Task.Delay( 2100 );
        lease.KeepAlive( TestHelper.Monitor ).ShouldBeTrue();
        lease.Token.ShouldNotBe( token, "Half over: renewed, and each lease state is a new commit." );
        lease.ExpiresAt.ShouldBeGreaterThan( expiresAt );
        lease.Duration.ShouldBe( TimeSpan.FromSeconds( 4 ), "A renewal asks for the duration the lease already had." );
        lease.IsLost.ShouldBeFalse();

        lease.Release( TestHelper.Monitor ).ShouldBeTrue();
    }

    /// <summary>
    /// Losing the race is what IsLost is for, and it is definitive: regaining the lock would be a NEW lease,
    /// hence a window during which somebody else worked on what this one was protecting.
    /// </summary>
    [Test]
    public async Task KeepAlive_that_lost_the_lock_is_definitive_Async()
    {
        var (a, b) = await ArrangeTwoAsync();
        using var disposeA = a;
        using var disposeB = b;

        a.Stack.GetLock( TestHelper.Monitor, "publish", out var aLock ).ShouldBeTrue();
        aLock.TryAcquire( TestHelper.Monitor, TimeSpan.FromSeconds( 2 ), out var aLease, out _ )
             .ShouldBe( AcquireResult.Acquired );
        aLease.ShouldNotBeNull();
        aLease.IsLost.ShouldBeFalse();

        // Another client takes the reference over (only a force push can do that while the lease is alive).
        ForcePushLockRef( b, "refs/ckli-locks/publish", HeadTip( b ) );

        await Task.Delay( 1100 );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            aLease.KeepAlive( TestHelper.Monitor ).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Lost the lock 'refs/ckli-locks/publish'" ) );
        }
        aLease.IsLost.ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            aLease.KeepAlive( TestHelper.Monitor ).ShouldBeFalse();
            logs.ShouldBeEmpty( "Sticky: finding out again would cost a round trip and could not change the answer." );
        }
    }

    /// <summary>
    /// A step that offers no checkpoint at all - a build that takes as long as it takes - must not force the
    /// lease to be sized on it.
    /// </summary>
    [Test]
    public async Task KeepAliveWhileAsync_renews_while_the_work_runs_Async()
    {
        using var one = await ArrangeOneAsync();

        one.Stack.GetLock( TestHelper.Monitor, "publish", out var theLock ).ShouldBeTrue();
        theLock.TryAcquire( TestHelper.Monitor, TimeSpan.FromSeconds( 2 ), out var lease, out _ )
               .ShouldBe( AcquireResult.Acquired );
        lease.ShouldNotBeNull();
        var token = lease.Token;
        var expiresAt = lease.ExpiresAt;

        (await lease.KeepAliveWhileAsync( TestHelper.Monitor, LongWorkAsync() )).ShouldBe( 3712 );

        lease.IsLost.ShouldBeFalse();
        lease.Token.ShouldNotBe( token );
        lease.ExpiresAt.ShouldBeGreaterThan( expiresAt );
        lease.IsExpired.ShouldBeFalse( "The work outlived the initial lease: it has been renewed on the way." );

        lease.Release( TestHelper.Monitor ).ShouldBeTrue();

        static async Task<int> LongWorkAsync()
        {
            await Task.Delay( 3000 );
            return 3712;
        }
    }

    #region Helpers

    /// <summary>
    /// A clone of the "One" Stack: <see cref="Stack"/> is opened on it. Several of these in one test are
    /// the several developers of a Stack.
    /// </summary>
    sealed class Clone : IDisposable
    {
        public required StackRepository Stack { get; init; }

        public void Dispose() => Stack.Dispose();
    }

    // Both arranges take the CallerMemberName: it names the "Cloned/<test-name>" folder. They must be
    // called ONCE per test - EnsureCleanFolder wipes that folder (and would hit the Git handles that an
    // already opened clone holds) and OpenRemotes re-extracts the bare remote, which would throw away
    // everything a clone has pushed to it.
    static async Task<Clone> ArrangeOneAsync( [CallerMemberName] string? name = null )
    {
        return (await ArrangeAsync( 1, name ))[0];
    }

    static async Task<(Clone A, Clone B)> ArrangeTwoAsync( [CallerMemberName] string? name = null )
    {
        var clones = await ArrangeAsync( 2, name );
        return (clones[0], clones[1]);
    }

    /// <summary>
    /// One clean test folder, one freshly extracted "One" bare remote, and <paramref name="developerCount"/>
    /// clones of it in their own "A"/"B"/... sub folders: the several developers of one Stack.
    /// </summary>
    static async Task<Clone[]> ArrangeAsync( int developerCount, string? name )
    {
        var context = TestEnv.EnsureCleanFolder( name );
        var one = TestEnv.OpenRemotes( "One" );
        var clones = new Clone[developerCount];
        for( int i = 0; i < developerCount; ++i )
        {
            // The Stack registry is machine global: a second clone of the same Stack needs it cleared.
            if( i > 0 ) StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
            var target = context.ChangeDirectory( ((char)('A' + i)).ToString() );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri )).ShouldBeTrue();
            clones[i] = new Clone
            {
                Stack = StackRepository.TryOpenFromPath( TestHelper.Monitor,
                                                         target.ChangeDirectory( "One" ),
                                                         out _,
                                                         skipPullStack: true )
                                       .ShouldNotBeNull()
            };
        }
        return clones;
    }

    /// <summary>
    /// The lock references that the Stack remote advertises.
    /// </summary>
    static string[] RemoteLockRefs( Clone c )
    {
        var git = c.Stack.GitRepository;
        git.GetRemote( TestHelper.Monitor, "origin", forWrite: false, out var remote, out var creds ).ShouldBeTrue();
        return git.Repository.Network
                  .ListReferences( remote, ( url, user, types ) => creds )
                  .Select( r => r.CanonicalName )
                  .Where( n => n.Contains( StackRepository.LockSegmentName ) )
                  .ToArray();
    }

    static ObjectId HeadTip( Clone c ) => c.Stack.GitRepository.Repository.Head.Tip.Id;

    /// <summary>
    /// Force pushes a reference: this is what no legitimate client ever does - it is how a test arranges
    /// "somebody else owns the reference now".
    /// </summary>
    static void ForcePushLockRef( Clone c, string refName, ObjectId target )
    {
        var git = c.Stack.GitRepository;
        git.GetRemote( TestHelper.Monitor, "origin", forWrite: true, out var remote, out var creds ).ShouldBeTrue();
        git.Repository.Refs.Add( refName, target, allowOverwrite: true );
        git.Repository.Network.Push( remote,
                                     $"+{refName}:{refName}",
                                     new PushOptions { CredentialsProvider = ( url, user, types ) => creds } );
    }

    #endregion
}
