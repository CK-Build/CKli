using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// "ckli world lock" and "ckli world unlock" are two separate processes, so the lease cannot live in
/// memory between them: the remote reference IS the state, and the lock recognizes its own by the clone
/// that took it. Nothing is written locally.
/// </summary>
[TestFixture]
public class WorldLockCommandTests
{
    // Every test pushes lock references to the "file://" remote.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    /// <summary>
    /// The lock reference lives in the Stack repository whatever it protects, so the World is part of its
    /// name: two developers publishing two different Worlds of one Stack must not wait for each other.
    /// </summary>
    [Test]
    public async Task world_lock_then_unlock_Async()
    {
        var (a, _) = await ArrangeAsync();

        (await Exec( a, "world", "lock", "publish" )).ShouldBeTrue();
        RemoteLockRefs( a ).ShouldBe( new[] { "refs/ckli-locks/One-publish" },
                                      customMessage: "The lock name carries the World name." );

        (await Exec( a, "world", "unlock", "publish" )).ShouldBeTrue();
        RemoteLockRefs( a ).ShouldBeEmpty();
    }

    /// <summary>
    /// The whole point of the owner identity: the process that took the lock is gone, so a second "world
    /// lock" from the same clone must extend it instead of reporting it taken.
    /// </summary>
    [Test]
    public async Task world_lock_renews_the_lock_of_this_clone_Async()
    {
        var (a, _) = await ArrangeAsync();

        (await Exec( a, "world", "lock", "publish", "--duration", "5" )).ShouldBeTrue();
        var firstToken = RemoteLockRef( a );

        (await Exec( a, "world", "lock", "publish", "--duration", "20" ))
            .ShouldBeTrue( "Already ours: renewed, not refused." );
        RemoteLockRef( a ).ShouldNotBe( firstToken, "Each lease state is a new commit on the chain." );

        (await Exec( a, "world", "unlock", "publish" )).ShouldBeTrue();
        RemoteLockRefs( a ).ShouldBeEmpty();
    }

    [Test]
    public async Task world_lock_fails_and_names_the_holder_Async()
    {
        var (a, b) = await ArrangeAsync();

        (await Exec( a, "world", "lock", "publish", "--duration", "30" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await Exec( b, "world", "lock", "publish" ))
                .ShouldBeFalse( "Somebody else holds it: the caller asked for the lock and did not get it." );
            logs.ShouldContain( l => l.Contains( "Unable to lock 'refs/ckli-locks/One-publish'" )
                                     && l.Contains( "it is held by" )
                                     // The holder and when it frees itself, unless it renews.
                                     && l.Contains( "on " + System.Environment.MachineName )
                                     && l.Contains( "frees itself" ) );
        }
        // The other developer's lock is untouched and still his.
        RemoteLockRefs( a ).ShouldBe( new[] { "refs/ckli-locks/One-publish" } );
        (await Exec( a, "world", "unlock", "publish" )).ShouldBeTrue();
        (await Exec( b, "world", "lock", "publish" )).ShouldBeTrue( "Released: the second developer gets it." );
        (await Exec( b, "world", "unlock", "publish" )).ShouldBeTrue();
    }

    [Test]
    public async Task world_unlock_always_succeeds_and_never_takes_another_lock_Async()
    {
        var (a, b) = await ArrangeAsync();

        // Nothing to release is not an error.
        (await Exec( a, "world", "unlock", "publish" )).ShouldBeTrue();

        (await Exec( a, "world", "lock", "publish", "--duration", "30" )).ShouldBeTrue();
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await Exec( b, "world", "unlock", "publish" ))
                .ShouldBeTrue( "Unlocking always succeeds: there was simply nothing of ours to release." );
            logs.ShouldContain( l => l.Contains( "is not ours to release" ) );
        }
        RemoteLockRefs( a ).ShouldBe( new[] { "refs/ckli-locks/One-publish" },
                                      customMessage: "The holder's lock is intact." );

        (await Exec( a, "world", "unlock", "publish" )).ShouldBeTrue();
        // Releasing twice is idempotent.
        (await Exec( a, "world", "unlock", "publish" )).ShouldBeTrue();
    }

    [Test]
    public async Task two_names_are_two_locks_Async()
    {
        var (a, b) = await ArrangeAsync();

        (await Exec( a, "world", "lock", "publish" )).ShouldBeTrue();
        (await Exec( b, "world", "lock", "build" )).ShouldBeTrue( "A different name is a different lock." );

        RemoteLockRefs( a ).OrderBy( x => x ).ToArray()
                           .ShouldBe( new[] { "refs/ckli-locks/One-build", "refs/ckli-locks/One-publish" } );

        (await Exec( a, "world", "unlock", "publish" )).ShouldBeTrue();
        (await Exec( b, "world", "unlock", "build" )).ShouldBeTrue();
    }

    [Test]
    public async Task an_invalid_duration_or_name_is_refused_Async()
    {
        var (a, _) = await ArrangeAsync();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await Exec( a, "world", "lock", "publish", "--duration", "0" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Invalid --duration value." ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // MaxLeaseDuration is one hour: a lock a crashed client holds for longer is not a lock.
            (await Exec( a, "world", "lock", "publish", "--duration", "120" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Invalid --duration value." ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // A lock named "a" and a lock named "a/b" are a permanent directory/file conflict on the remote.
            (await Exec( a, "world", "lock", "pub/lish" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "it must not contain a '/'" ) );
        }
        RemoteLockRefs( a ).ShouldBeEmpty( "Nothing has been locked." );
    }

    #region Helpers

    static ValueTask<bool> Exec( CKliEnv context, params string[] args )
        => CKliCommands.ExecAsync( TestHelper.Monitor, context, args );

    /// <summary>
    /// One clean test folder, one freshly extracted "One" bare remote and two clones of it: the two
    /// developers of a Stack. Called ONCE per test (it wipes the folder and re-extracts the remote).
    /// </summary>
    static async Task<(CKliEnv A, CKliEnv B)> ArrangeAsync( [CallerMemberName] string? name = null )
    {
        var context = TestEnv.EnsureCleanFolder( name );
        var one = TestEnv.OpenRemotes( "One" );
        var a = context.ChangeDirectory( "A" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, a, "clone", one.StackUri )).ShouldBeTrue();
        // The Stack registry is machine global: a second clone of the same Stack needs it cleared.
        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var b = context.ChangeDirectory( "B" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, b, "clone", one.StackUri )).ShouldBeTrue();
        return (a.ChangeDirectory( "One" ), b.ChangeDirectory( "One" ));
    }

    /// <summary>
    /// The lock references that the Stack remote advertises. The Stack is opened and closed around the
    /// read: a command run next must not find a Git handle held open on its working folder.
    /// </summary>
    static string[] RemoteLockRefs( CKliEnv inStack )
    {
        using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                         .ShouldNotBeNull();
        var git = stack.GitRepository;
        git.GetRemote( TestHelper.Monitor, "origin", forWrite: false, out var remote, out var creds ).ShouldBeTrue();
        return git.Repository.Network
                  .ListReferences( remote, ( url, user, types ) => creds )
                  .Select( r => r.CanonicalName )
                  .Where( n => n.Contains( StackRepository.LockSegmentName ) )
                  .ToArray();
    }

    /// <summary>
    /// The object id that the single advertised lock reference points to.
    /// </summary>
    static string RemoteLockRef( CKliEnv inStack )
    {
        using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                         .ShouldNotBeNull();
        var git = stack.GitRepository;
        git.GetRemote( TestHelper.Monitor, "origin", forWrite: false, out var remote, out var creds ).ShouldBeTrue();
        return git.Repository.Network
                  .ListReferences( remote, ( url, user, types ) => creds )
                  .Single( r => r.CanonicalName.Contains( StackRepository.LockSegmentName ) )
                  .TargetIdentifier;
    }

    #endregion
}
