using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// A world definition file can declare the other Stacks it uses with &lt;Reference Url="..." /&gt; elements.
/// They are exposed by <see cref="WorldDefinitionFile.References"/> and only the "ckli clone" command
/// honors them: it clones them next to the Stack, recursively.
/// </summary>
[TestFixture]
public class StackReferenceTests
{
    // The arrange of the clone test pushes to the CKt-Stack remote.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [Test]
    public async Task references_can_be_direct_children_or_grouped_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );
        var withIssues = TestEnv.OpenRemotes( "WithIssues" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );
        var xmlPath = context.CurrentDirectory.Combine( ".PublicStack/CKt.xml" );

        ReadReferences( context ).ShouldBeEmpty( "No <Reference /> by default." );

        // A direct child and a grouped one are both collected, in document order.
        SetReferences( xmlPath,
                       $"""<Reference Url="{one.StackUri}" />""",
                       $"""
                        <References>
                          <Reference Url="{withIssues.StackUri}" DefaultClone="false" />
                        </References>
                        """ );
        var references = ReadReferences( context );
        references.Count.ShouldBe( 2 );
        references[0].Attribute( XNames.Url )!.Value.ShouldBe( one.StackUri.ToString() );
        ((bool?)references[0].Attribute( XNames.DefaultClone )).ShouldBeNull( "Not specified: defaults to true." );
        references[1].Attribute( XNames.Url )!.Value.ShouldBe( withIssues.StackUri.ToString() );
        ((bool?)references[1].Attribute( XNames.DefaultClone )).ShouldBe( false );

        // A <Reference /> is not a layout element: no "Unexpected element" warning and no impact on the Layout.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                                             .ShouldNotBeNull();
            var definitionFile = stack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor ).ShouldNotBeNull();
            definitionFile.ReadLayout( TestHelper.Monitor ).ShouldNotBeNull().Count.ShouldBe( 1 );
            logs.ShouldNotContain( l => l.Contains( "Unexpected element" ) );
        }
    }

    [Test]
    public async Task a_public_Stack_cannot_reference_a_private_one_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );

        // ckli clone file:///.../CKt-Stack (a public Stack: it is cloned in ".PublicStack").
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );
        var xmlPath = context.CurrentDirectory.Combine( ".PublicStack/CKt.xml" );

        // Private="false" is the default: this is fine.
        SetReferences( xmlPath, $"""<Reference Url="{one.StackUri}" Private="false" />""" );
        ReadReferences( context ).Count.ShouldBe( 1 );

        SetReferences( xmlPath, $"""<Reference Url="{one.StackUri}" Private="true" />""" );
        using( TestHelper.Monitor.CollectEntries( out var entries ) )
        {
            using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                                             .ShouldNotBeNull();
            stack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor ).ShouldBeNull();
            // The error is an exception: it is logged by LocalWorldName.DoLoadDefinitionFile.
            entries.ShouldContain( e => e.Exception != null
                                        && e.Exception.Message.Contains( "A public Stack cannot reference a private one." ) );
        }
        // And the World cannot be loaded at all.
        {
            var (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor,
                                                                       context,
                                                                       out var error,
                                                                       skipPullStack: true,
                                                                       withPlugins: false );
            error.ShouldBeTrue();
            stack.ShouldBeNull();
            world.ShouldBeNull();
        }
    }

    [Test]
    public async Task clone_clones_the_references_next_to_the_Stack_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );

        // The arrange clones the CKt-Stack remote directly to be able to push the <Reference /> to it.
        var arrangePath = context.CurrentDirectory.AppendPart( "Arrange" );
        using var arrange = GitRepository.Clone( TestHelper.Monitor,
                                                 new GitRepositoryKey( context.SecretsStore, ckt.StackUri, isPublic: true ),
                                                 context.Committer,
                                                 arrangePath,
                                                 arrangePath.LastPart ).ShouldNotBeNull();
        var xmlPath = arrange.WorkingFolder.AppendPart( "CKt.xml" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri, "--with-ref-clone", "--without-ref-clone" ))
            .ShouldBeFalse( "The 2 flags are mutually exclusive." );

        using( TestHelper.Monitor.OpenInfo( "The CKt-Stack remote now references the One-Stack." ) )
        {
            PushArrange( arrange, xmlPath, $"""<Reference Url="{one.StackUri}" />""" );
        }
        // By default the reference is cloned next to the Stack, with its own world repositories.
        await CloneIntoAsync( context, ckt.StackUri, "Default", oneIsCloned: true );
        // --without-ref-clone ignores it.
        await CloneIntoAsync( context, ckt.StackUri, "Without", oneIsCloned: false, "--without-ref-clone" );

        using( TestHelper.Monitor.OpenInfo( "The reference is now DefaultClone=\"false\"." ) )
        {
            PushArrange( arrange, xmlPath, $"""<Reference Url="{one.StackUri}" DefaultClone="false" />""" );
        }
        // DefaultClone="false" is skipped by default...
        await CloneIntoAsync( context, ckt.StackUri, "NoDefaultClone", oneIsCloned: false );
        // ...and cloned by --with-ref-clone.
        await CloneIntoAsync( context, ckt.StackUri, "WithRef", oneIsCloned: true, "--with-ref-clone" );

        static void PushArrange( GitRepository arrange, NormalizedPath xmlPath, string reference )
        {
            SetReferences( xmlPath, reference );
            arrange.Commit( TestHelper.Monitor, "Reference update." ).ShouldBe( CommitResult.Committed );
            arrange.PushBranch( TestHelper.Monitor, arrange.Repository.Head, autoCreateRemoteBranch: true ).ShouldBeTrue();
        }

        static async Task CloneIntoAsync( CKliEnv context, Uri stackUri, string folderName, bool oneIsCloned, string? flag = null )
        {
            using( TestHelper.Monitor.OpenInfo( $"ckli clone {stackUri} in '{folderName}' {flag}" ) )
            {
                // The Stacks cloned by the previous steps are still on the disk: forgetting them enables
                // the very same Stacks to be cloned again in another folder.
                StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
                var target = context.ChangeDirectory( folderName );
                bool success = flag == null
                                ? await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", stackUri )
                                : await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", stackUri, flag );
                success.ShouldBeTrue();
                Directory.Exists( target.CurrentDirectory.Combine( "CKt/.PublicStack" ) ).ShouldBeTrue();
                Directory.Exists( target.CurrentDirectory.Combine( "One/.PublicStack" ) ).ShouldBe( oneIsCloned );
                if( oneIsCloned )
                {
                    Directory.Exists( target.CurrentDirectory.Combine( "One/OneRepo" ) )
                             .ShouldBeTrue( "The referenced Stack's world repositories are cloned." );
                }
            }
        }
    }

    [Test]
    public async Task references_are_cloned_recursively_and_a_cycle_is_handled_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );
        var withIssues = TestEnv.OpenRemotes( "WithIssues" );

        // CKt -> One -> WithIssues -> CKt: the cycle must not loop forever.
        ArrangeReference( ckt.StackUri, "CKt", one.StackUri );
        ArrangeReference( one.StackUri, "One", withIssues.StackUri );
        ArrangeReference( withIssues.StackUri, "WithIssues", ckt.StackUri );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", ckt.StackUri )).ShouldBeTrue();

        Directory.Exists( target.CurrentDirectory.Combine( "CKt/.PublicStack" ) ).ShouldBeTrue();
        Directory.Exists( target.CurrentDirectory.Combine( "One/.PublicStack" ) ).ShouldBeTrue();
        Directory.Exists( target.CurrentDirectory.Combine( "WithIssues/.PublicStack" ) ).ShouldBeTrue( "Reached through One." );
        Directory.EnumerateDirectories( target.CurrentDirectory )
                 .Count()
                 .ShouldBe( 3, "The cycle back to CKt cloned nothing more." );

        void ArrangeReference( Uri stackUri, string name, Uri referenced )
        {
            ArrangeStack( context, stackUri, name,
                          git => SetReferences( git.WorkingFolder.AppendPart( $"{name}.xml" ),
                                                $"""<Reference Url="{referenced}" />""" ) );
        }
    }

    /// <summary>
    /// Clones a Stack remote directly (into an "Arrange/" folder), applies an edit to its working
    /// folder and pushes it: this is how a &lt;Reference /&gt; reaches a remote that "ckli clone" then reads.
    /// </summary>
    static void ArrangeStack( CKliEnv context, Uri stackUri, string name, Action<GitRepository> editor )
    {
        var path = context.CurrentDirectory.Combine( "Arrange" ).AppendPart( name );
        using var git = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, stackUri, isPublic: true ),
                                             context.Committer,
                                             path,
                                             path.LastPart ).ShouldNotBeNull();
        editor( git );
        git.Commit( TestHelper.Monitor, "Arrange." ).ShouldBe( CommitResult.Committed );
        git.PushBranch( TestHelper.Monitor, git.Repository.Head, autoCreateRemoteBranch: true ).ShouldBeTrue();
    }

    [Test]
    public async Task clone_honors_the_reference_LTSName_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );

        // The One-Stack gets a "@net8" LTS world with the same single repository as its default one...
        ArrangeStack( context, one.StackUri, "One", git =>
        {
            File.WriteAllText( git.WorkingFolder.AppendPart( "One@net8.xml" ),
                               """
                               <One LTSName="@net8">
                                 <Repository Url="OneRepo" />
                               </One>
                               """ );
        } );
        // ...and the CKt-Stack references that world.
        ArrangeStack( context, ckt.StackUri, "CKt", git =>
        {
            SetReferences( git.WorkingFolder.AppendPart( "CKt.xml" ),
                           $"""<Reference Url="{one.StackUri}" LTSName="@net8" />""" );
        } );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", ckt.StackUri )).ShouldBeTrue();

        Directory.Exists( target.CurrentDirectory.Combine( "One/.PublicStack" ) ).ShouldBeTrue();
        Directory.Exists( target.CurrentDirectory.Combine( "One/@net8/OneRepo" ) )
                 .ShouldBeTrue( "The LTS world's repositories are cloned in its own folder." );
        Directory.Exists( target.CurrentDirectory.Combine( "One/OneRepo" ) )
                 .ShouldBeFalse( "The default world's repositories are not the ones that are used." );
    }

    [Test]
    public async Task clone_fails_when_the_referenced_Stack_has_no_such_LTS_world_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );

        // The One-Stack has no LTS world at all: only its default one.
        ArrangeStack( context, ckt.StackUri, "CKt", git =>
        {
            SetReferences( git.WorkingFolder.AppendPart( "CKt.xml" ),
                           $"""<Reference Url="{one.StackUri}" LTSName="@net8" />""" );
        } );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", ckt.StackUri )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Stack 'One' has no '@net8' Long Term Support world." ) );
        }
    }

    [Test]
    public async Task world_reference_set_creates_then_merges_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );
        var withIssues = TestEnv.OpenRemotes( "WithIssues" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );

        // Creation: no optional attribute.
        (await Set( context, one.StackUri )).ShouldBeTrue();
        var references = ReadReferences( context );
        references.Count.ShouldBe( 1 );
        references[0].Attribute( XNames.Url )!.Value.ShouldBe( one.StackUri.ToString() );
        references[0].Attribute( XNames.DefaultClone ).ShouldBeNull();
        references[0].Attribute( XNames.Private ).ShouldBeNull();
        CheckStackCommit( context, "Set reference to Stack" );

        // --no-default-clone sets DefaultClone="false".
        (await Set( context, one.StackUri, "--no-default-clone" )).ShouldBeTrue();
        references = ReadReferences( context );
        references.Count.ShouldBe( 1, "The reference has been updated, not duplicated." );
        ((bool?)references[0].Attribute( XNames.DefaultClone )).ShouldBe( false );

        // This merges: a "set" that names no attribute leaves the existing ones as they are.
        (await Set( context, one.StackUri )).ShouldBeTrue();
        ((bool?)ReadReferences( context )[0].Attribute( XNames.DefaultClone )).ShouldBe( false );

        // --default-clone removes the attribute since true is its default value.
        (await Set( context, one.StackUri, "--default-clone" )).ShouldBeTrue();
        ReadReferences( context )[0].Attribute( XNames.DefaultClone ).ShouldBeNull();

        // Another url is another reference.
        (await Set( context, withIssues.StackUri, "--no-default-clone" )).ShouldBeTrue();
        references = ReadReferences( context );
        references.Count.ShouldBe( 2 );
        references[0].Attribute( XNames.Url )!.Value.ShouldBe( one.StackUri.ToString() );
        references[1].Attribute( XNames.Url )!.Value.ShouldBe( withIssues.StackUri.ToString() );
    }

    [Test]
    public async Task world_reference_set_handles_the_optional_LTSName_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );

        // LTSName has no default value: when no --lts-name is specified, the attribute is simply absent.
        (await Set( context, one.StackUri )).ShouldBeTrue();
        ReadReferences( context )[0].Attribute( XNames.LTSName ).ShouldBeNull();

        (await Set( context, one.StackUri, "--lts-name", "@net8" )).ShouldBeTrue();
        var references = ReadReferences( context );
        references.Count.ShouldBe( 1, "The reference has been updated, not duplicated." );
        references[0].Attribute( XNames.LTSName )!.Value.ShouldBe( "@net8" );

        // This merges: the other attributes are set without touching the LTSName.
        (await Set( context, one.StackUri, "--no-default-clone" )).ShouldBeTrue();
        references = ReadReferences( context );
        references[0].Attribute( XNames.LTSName )!.Value.ShouldBe( "@net8" );
        ((bool?)references[0].Attribute( XNames.DefaultClone )).ShouldBe( false );

        // --lts-name replaces it and --default-world removes it.
        (await Set( context, one.StackUri, "--lts-name", "@net9" )).ShouldBeTrue();
        ReadReferences( context )[0].Attribute( XNames.LTSName )!.Value.ShouldBe( "@net9" );
        (await Set( context, one.StackUri, "--default-world" )).ShouldBeTrue();
        ReadReferences( context )[0].Attribute( XNames.LTSName ).ShouldBeNull();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await Set( context, one.StackUri, "--lts-name", "@net8", "--default-world" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "--lts-name and flag --default-world are mutually exclusive" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // A LTS name must start with '@' and be lower case.
            (await Set( context, one.StackUri, "--lts-name", "Net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Invalid --lts-name 'Net8'." ) );
        }
        ReadReferences( context )[0].Attribute( XNames.LTSName ).ShouldBeNull( "Nothing has been written." );
    }

    [Test]
    public async Task an_invalid_LTSName_prevents_the_world_to_be_loaded_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );
        var xmlPath = context.CurrentDirectory.Combine( ".PublicStack/CKt.xml" );

        SetReferences( xmlPath, $"""<Reference Url="{one.StackUri}" LTSName="@net8" />""" );
        ReadReferences( context ).Count.ShouldBe( 1 );

        // Like the 2 boolean attributes, an invalid LTSName throws: it never reaches a consumer.
        SetReferences( xmlPath, $"""<Reference Url="{one.StackUri}" LTSName="net8" />""" );
        using( TestHelper.Monitor.CollectEntries( out var entries ) )
        {
            using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                                             .ShouldNotBeNull();
            stack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor ).ShouldBeNull();
            entries.ShouldContain( e => e.Exception != null
                                        && e.Exception.Message.Contains( """Invalid LTSName="net8".""" ) );
        }
    }

    [Test]
    public async Task world_reference_set_refuses_invalid_references_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await Set( context, one.StackUri, "--private", "--public" ))
                .ShouldBeFalse( "The 2 flags are mutually exclusive." );
            logs.ShouldContain( l => l.Contains( "--private and --public are mutually exclusive" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // A reference names a Stack: the url must have the "-Stack" suffix.
            (await Set( context, new Uri( ckt.StackUri.ToString().Replace( "-Stack", "-NotAStack" ) ) )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "must have '-Stack' suffix" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await Set( context, ckt.StackUri )).ShouldBeFalse( "A Stack cannot reference itself." );
            logs.ShouldContain( l => l.Contains( "cannot reference itself" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // This is the invariant that ReadReferences enforces by throwing: writing it would produce a
            // world definition file that can no more be loaded.
            (await Set( context, one.StackUri, "--private" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "a public Stack cannot reference a private one." ) );
        }
        ReadReferences( context ).ShouldBeEmpty( "Nothing has been written." );
    }

    [Test]
    public async Task world_reference_set_resolves_the_name_of_a_locally_cloned_Stack_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri )).ShouldBeTrue();
        var cktContext = context.ChangeDirectory( "CKt" );

        // The stack name of a Stack that is cloned on this machine is resolved to its url.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktContext, "world", "reference", "set", "One" )).ShouldBeTrue();
        var references = ReadReferences( cktContext );
        references.Count.ShouldBe( 1 );
        references[0].Attribute( XNames.Url )!.Value.ShouldBe( one.StackUri.ToString() );

        // The repository name works too and updates the very same reference.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktContext, "world", "reference", "set", "One-Stack", "--no-default-clone" ))
            .ShouldBeTrue();
        references = ReadReferences( cktContext );
        references.Count.ShouldBe( 1 );
        ((bool?)references[0].Attribute( XNames.DefaultClone )).ShouldBe( false );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktContext, "world", "reference", "set", "NotCloned" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "no Stack named" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // The One-Stack is cloned in a ".PublicStack" folder here: this is the ground truth.
            (await Set( cktContext, one.StackUri, "--private" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "The --private flag contradicts it." ) );
        }
    }

    [Test]
    public async Task world_reference_remove_is_idempotent_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );
        var withIssues = TestEnv.OpenRemotes( "WithIssues" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );

        (await Set( context, one.StackUri )).ShouldBeTrue();
        (await Set( context, withIssues.StackUri )).ShouldBeTrue();
        ReadReferences( context ).Count.ShouldBe( 2 );

        // By stack name.
        (await Remove( context, "One" )).ShouldBeTrue();
        var references = ReadReferences( context );
        references.Count.ShouldBe( 1 );
        references[0].Attribute( XNames.Url )!.Value.ShouldBe( withIssues.StackUri.ToString() );
        CheckStackCommit( context, "Removed reference to Stack" );

        // By url.
        (await Remove( context, withIssues.StackUri.ToString() )).ShouldBeTrue();
        ReadReferences( context ).ShouldBeEmpty();

        // Removing what is not there is not an error.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await Remove( context, "One" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "No <Reference /> matching 'One'" ) );
        }
    }

    [Test]
    public async Task world_reference_set_and_remove_handle_the_References_group_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );
        var withIssues = TestEnv.OpenRemotes( "WithIssues" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );
        var xmlPath = context.CurrentDirectory.Combine( ".PublicStack/CKt.xml" );

        SetReferences( xmlPath,
                       $"""
                        <References>
                          <Reference Url="{one.StackUri}" />
                        </References>
                        """ );
        // A new reference joins the existing group.
        (await Set( context, withIssues.StackUri )).ShouldBeTrue();
        var references = ReadReferences( context );
        references.Count.ShouldBe( 2 );
        references.ShouldAllBe( e => e.Parent!.Name == XNames.References );

        // The group that becomes empty is removed.
        (await Remove( context, "One" )).ShouldBeTrue();
        (await Remove( context, "WithIssues" )).ShouldBeTrue();
        ReadReferences( context ).ShouldBeEmpty();
        XDocument.Load( xmlPath ).Root!.Elements( XNames.References ).ShouldBeEmpty();
    }

    [Test]
    public async Task world_reference_list_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var ckt = TestEnv.OpenRemotes( "CKt" );
        var one = TestEnv.OpenRemotes( "One" );
        var display = (StringScreen)context.Screen;

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", ckt.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "CKt" );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "world", "reference", "list" )).ShouldBeTrue();
        display.ToString().ShouldContain( "has no <Reference />" );

        (await Set( context, one.StackUri, "--no-default-clone" )).ShouldBeTrue();
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "world", "reference", "list" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( "1 reference(s) in world 'CKt':" );
        text.ShouldContain( one.StackUri.ToString() );
        text.ShouldContain( "no-clone" );
        text.ShouldContain( "public" );
        text.ShouldContain( "(default world)", customMessage: "No LTSName attribute." );
        text.ShouldContain( "(not cloned here)", customMessage: "The One-Stack has not been cloned by this test." );

        (await Set( context, one.StackUri, "--lts-name", "@net8" )).ShouldBeTrue();
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "world", "reference", "list" )).ShouldBeTrue();
        display.ToString().ShouldContain( "@net8" );
    }

    static ValueTask<bool> Set( CKliEnv context, Uri stackUrl, params string[] flags )
    {
        return CKliCommands.ExecAsync( TestHelper.Monitor,
                                       context,
                                       ["world", "reference", "set", stackUrl, .. flags] );
    }

    static ValueTask<bool> Remove( CKliEnv context, string nameOrUrl )
    {
        return CKliCommands.ExecAsync( TestHelper.Monitor, context, "world", "reference", "remove", nameOrUrl );
    }

    /// <summary>
    /// The commands commit their change in the Stack repository: no "push --stack-only" is required
    /// for the change to be durable locally.
    /// </summary>
    static void CheckStackCommit( CKliEnv context, string expectedMessage )
    {
        using var git = new LibGit2Sharp.Repository( context.CurrentDirectory.AppendPart( ".PublicStack" ) );
        git.Head.Tip.Message.ShouldContain( expectedMessage );
    }

    /// <summary>
    /// The underlying elements of the <see cref="WorldDefinitionFile.References"/>: these tests assert that
    /// an attribute is absent rather than that it has its default value, and <see cref="WorldReference"/>
    /// cannot express that.
    /// </summary>
    static IReadOnlyList<XElement> ReadReferences( CKliEnv context )
    {
        using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                                         .ShouldNotBeNull();
        return stack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor )
                                     .ShouldNotBeNull()
                                     .References
                                     .Select( r => r.XElement )
                                     .ToList();
    }

    /// <summary>
    /// Replaces the &lt;Reference /&gt; and &lt;References&gt; elements of a world definition file.
    /// </summary>
    static void SetReferences( NormalizedPath xmlPath, params string[] references )
    {
        var doc = XDocument.Load( xmlPath );
        var root = doc.Root.ShouldNotBeNull();
        root.Elements( XNames.Reference ).Remove();
        root.Elements( XNames.References ).Remove();
        foreach( var r in references )
        {
            root.Add( XElement.Parse( r ) );
        }
        // Not doc.Save(): it would write a BOM and an xml declaration that the fixture doesn't have.
        File.WriteAllText( xmlPath, doc.ToString() );
    }
}
