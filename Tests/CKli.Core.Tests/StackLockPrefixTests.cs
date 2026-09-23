using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// The LockPrefix attribute of a World definition file is the prefix of the Git references that lock a Stack.
/// <para>
/// Its presence is the discriminator: absent means that nobody has asked for a lock yet, and the first one to
/// do so probes the remote and RECORDS what it accepts - including the default prefix. Once recorded it is
/// never re-determined, because a client that picked its own would lock where no colleague is looking.
/// </para>
/// <para>
/// It is a <b>Stack</b> level setting: every World of a Stack locks in the one Stack repository, so whether a
/// reference name is accepted there is the same answer for all of them. Only the default World can carry it -
/// a copy on a LTS World could only diverge from the one that is actually used, and two clients that disagree
/// on the prefix would both take "the" lock and neither would see the other.
/// </para>
/// </summary>
[TestFixture]
public class StackLockPrefixTests
{
    // Determining the prefix probes the remote and records the result: both push.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [Test]
    public void IsValidLockPrefix_requires_the_refs_prefix_and_the_lock_segment()
    {
        StackRepository.DefaultLockPrefix.ShouldBe( "refs/ckli-locks" );

        StackRepository.IsValidLockPrefix( StackRepository.DefaultLockPrefix ).ShouldBeTrue();
        StackRepository.IsValidLockPrefix( "refs/notes/ckli-locks" ).ShouldBeTrue();
        StackRepository.IsValidLockPrefix( "refs/heads/ckli-locks" ).ShouldBeTrue();

        StackRepository.IsValidLockPrefix( null ).ShouldBeFalse();
        StackRepository.IsValidLockPrefix( "" ).ShouldBeFalse();
        // A lock reference is a "refs/" one: a loose name is not accepted.
        StackRepository.IsValidLockPrefix( "ckli-locks" ).ShouldBeFalse();
        StackRepository.IsValidLockPrefix( "locks/ckli-locks" ).ShouldBeFalse();
        // The last part must be the segment that makes a lock reference recognizable whatever the prefix is:
        // that single segment is what detects a client locking the same Stack under another prefix.
        StackRepository.IsValidLockPrefix( "refs/locks" ).ShouldBeFalse();
        StackRepository.IsValidLockPrefix( "refs/ckli-locks/" ).ShouldBeFalse();
        StackRepository.IsValidLockPrefix( "refs/ckli-locks/publish" ).ShouldBeFalse();
        // The Git reference name rules still apply.
        StackRepository.IsValidLockPrefix( "refs//ckli-locks" ).ShouldBeFalse();
        StackRepository.IsValidLockPrefix( "refs/x y/ckli-locks" ).ShouldBeFalse();
        StackRepository.IsValidLockPrefix( "refs/x~/ckli-locks" ).ShouldBeFalse();
    }

    /// <summary>
    /// A Stack that nobody has locked yet carries no LockPrefix. The first one to need it probes the remote
    /// and records what it accepts - the default prefix here, since a bare local remote accepts everything -
    /// so that every other developer uses that same one instead of probing on their own.
    /// </summary>
    [Test]
    public async Task an_undetermined_LockPrefix_is_probed_and_recorded_Async()
    {
        var context = await CloneOneAsync();

        using( var stack = OpenStack( context ) )
        {
            stack.GetLockPrefix( TestHelper.Monitor, out var recorded ).ShouldBeTrue();
            recorded.ShouldBeNull( "Nobody has asked for a lock on this Stack yet." );

            stack.EnsureLockPrefix( TestHelper.Monitor, out var lockPrefix ).ShouldBeTrue();
            lockPrefix.ShouldBe( StackRepository.DefaultLockPrefix );

            // Recorded, not merely resolved: the next reader finds it without probing anything.
            stack.GetLockPrefix( TestHelper.Monitor, out recorded ).ShouldBeTrue();
            recorded.ShouldBe( StackRepository.DefaultLockPrefix );
        }
        ReadLockPrefixAttribute( context, "One.xml" )
            .ShouldBe( StackRepository.DefaultLockPrefix, customMessage: "Written to the definition file." );

        // And pushed: a value only this clone knows would not make anybody agree.
        using var fresh = OpenStack( context );
        fresh.GitRepository.Repository.Head.TrackingDetails.AheadBy
             .ShouldBe( 0, "The Stack has been pushed." );
    }

    /// <summary>
    /// A LTS World carries no LockPrefix of its own: the lock prefix is resolved from the default World
    /// whatever the current World is.
    /// </summary>
    [Test]
    public async Task the_default_world_LockPrefix_is_the_Stack_lock_prefix_Async()
    {
        var context = await CloneOneAsync();
        SetLockPrefix( context, "One.xml", "refs/heads/ckli-locks" );
        WriteLTSWorld( context, "@net8", lockPrefix: null );

        using var stack = OpenStack( context );
        stack.WorldNames.Length.ShouldBe( 2 );
        stack.WorldNames.Single( w => !w.IsDefaultWorld )
             .LoadDefinitionFile( TestHelper.Monitor )
             .ShouldNotBeNull()
             .LockPrefix.ShouldBeNull( "A LTS World never carries the setting." );

        stack.GetLockPrefix( TestHelper.Monitor, out var lockPrefix ).ShouldBeTrue();
        lockPrefix.ShouldBe( "refs/heads/ckli-locks" );
    }

    [Test]
    public async Task an_invalid_LockPrefix_prevents_the_world_to_be_loaded_Async()
    {
        var context = await CloneOneAsync();
        // Valid as a Git reference name, but its last part is not the "ckli-locks" segment.
        SetLockPrefix( context, "One.xml", "refs/locks" );

        using( TestHelper.Monitor.CollectEntries( out var entries ) )
        {
            using var stack = OpenStack( context );
            stack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor ).ShouldBeNull();
            entries.ShouldContain( e => e.Exception != null
                                        && e.Exception.Message.Contains( """Invalid LockPrefix="refs/locks" attribute""" ) );
        }
        // A Stack whose stated prefix is unusable must be fixed, not silently fall back to the default one:
        // another client may be using the stated one.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            using var stack = OpenStack( context );
            stack.GetLockPrefix( TestHelper.Monitor, out var lockPrefix ).ShouldBeFalse();
            lockPrefix.ShouldBeNull( "No fallback to the default: a Stack that states an unusable prefix must be fixed." );
            logs.ShouldContain( l => l.Contains( "Unable to read the lock prefix of Stack 'One'" ) );
        }
    }

    /// <summary>
    /// The value here is perfectly valid: it is its presence on a LTS World that is refused.
    /// </summary>
    [Test]
    public async Task a_LockPrefix_on_a_LTS_world_prevents_it_to_be_loaded_Async()
    {
        var context = await CloneOneAsync();
        WriteLTSWorld( context, "@net8", lockPrefix: StackRepository.DefaultLockPrefix );

        using( TestHelper.Monitor.CollectEntries( out var entries ) )
        {
            using var stack = OpenStack( context );
            stack.WorldNames.Single( w => !w.IsDefaultWorld )
                 .LoadDefinitionFile( TestHelper.Monitor )
                 .ShouldBeNull();
            entries.ShouldContain( e => e.Exception != null
                                        && e.Exception.Message.Contains( "Only the default World can carry it" ) );
        }
    }

    /// <summary>
    /// "ckli world lts create" clones the default World's root element: the LockPrefix must not be propagated,
    /// otherwise the new World would hold a copy that a later change of the Stack's prefix leaves behind.
    /// </summary>
    [Test]
    public async Task lts_create_does_not_copy_the_LockPrefix_Async()
    {
        var context = await CloneOneAsync();
        SetLockPrefix( context, "One.xml", "refs/notes/ckli-locks" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "world", "lts", "create", "@net8" )).ShouldBeTrue();

        var ltsRoot = XDocument.Load( StackFolder( context ).Combine( "@net8/One@net8.xml" ) ).Root.ShouldNotBeNull();
        ltsRoot.Attribute( XNames.LTSName ).ShouldNotBeNull().Value.ShouldBe( "@net8" );
        ltsRoot.Attribute( XNames.LockPrefix )
               .ShouldBeNull( "The LockPrefix is a Stack level setting: the new LTS World must not copy it." );

        // The new World loads (a copy would have prevented it) and the Stack's prefix applies to it.
        using var stack = OpenStack( context );
        stack.WorldNames.Single( w => !w.IsDefaultWorld )
             .LoadDefinitionFile( TestHelper.Monitor )
             .ShouldNotBeNull();
        stack.GetLockPrefix( TestHelper.Monitor, out var lockPrefix ).ShouldBeTrue();
        lockPrefix.ShouldBe( "refs/notes/ckli-locks" );
    }

    // The CallerMemberName is the calling test's name: without it every test here would share this
    // helper's name as its "Cloned/<test-name>" folder.
    static async Task<CKliEnv> CloneOneAsync( [CallerMemberName] string? name = null )
    {
        var context = TestEnv.EnsureCleanFolder( name );
        var one = TestEnv.OpenRemotes( "One" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri )).ShouldBeTrue();
        return context.ChangeDirectory( "One" );
    }

    static StackRepository OpenStack( CKliEnv context )
    {
        return StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                              .ShouldNotBeNull();
    }

    static NormalizedPath StackFolder( CKliEnv context ) => context.CurrentDirectory.AppendPart( ".PublicStack" );

    /// <summary>
    /// The LockPrefix attribute as it is in the file, without going through the (validating) loader.
    /// </summary>
    static string? ReadLockPrefixAttribute( CKliEnv context, string fileName )
    {
        return XDocument.Load( StackFolder( context ).AppendPart( fileName ) )
                        .Root.ShouldNotBeNull()
                        .Attribute( XNames.LockPrefix )?.Value;
    }

    /// <summary>
    /// Sets the LockPrefix attribute of a world definition file and commits the Stack repository: "ckli world
    /// lts create" pulls the Stack, so the edit must not be left in the working folder.
    /// </summary>
    static void SetLockPrefix( CKliEnv context, string fileName, string lockPrefix )
    {
        var xmlPath = StackFolder( context ).AppendPart( fileName );
        var doc = XDocument.Load( xmlPath );
        doc.Root.ShouldNotBeNull().SetAttributeValue( XNames.LockPrefix, lockPrefix );
        // Not doc.Save(): it would write a BOM and an xml declaration that the fixture doesn't have.
        WriteAndCommit( context, xmlPath, doc.ToString(), $"Set LockPrefix in '{fileName}'." );
    }

    static void WriteLTSWorld( CKliEnv context, string ltsName, string? lockPrefix )
    {
        var attributes = lockPrefix != null ? $""" LockPrefix="{lockPrefix}" """.TrimEnd() : "";
        // A LTS world definition file is in the world's own "@ltsName/" folder of the Stack repository.
        var path = StackFolder( context ).AppendPart( ltsName ).AppendPart( $"One{ltsName}.xml" );
        Directory.CreateDirectory( path.RemoveLastPart() );
        WriteAndCommit( context,
                        path,
                        $"""
                         <One LTSName="{ltsName}"{attributes}>
                           <Repository Url="OneRepo" />
                         </One>
                         """,
                        $"Added '{ltsName}' world." );
    }

    static void WriteAndCommit( CKliEnv context, NormalizedPath path, string content, string message )
    {
        File.WriteAllText( path, content );
        using var stack = OpenStack( context );
        stack.Commit( TestHelper.Monitor, message ).ShouldBeTrue();
    }
}
