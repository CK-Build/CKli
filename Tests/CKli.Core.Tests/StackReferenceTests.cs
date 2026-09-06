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
        ArrangeReference( context, ckt.StackUri, "CKt", $"""<Reference Url="{one.StackUri}" />""" );
        ArrangeReference( context, one.StackUri, "One", $"""<Reference Url="{withIssues.StackUri}" />""" );
        ArrangeReference( context, withIssues.StackUri, "WithIssues", $"""<Reference Url="{ckt.StackUri}" />""" );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", ckt.StackUri )).ShouldBeTrue();

        Directory.Exists( target.CurrentDirectory.Combine( "CKt/.PublicStack" ) ).ShouldBeTrue();
        Directory.Exists( target.CurrentDirectory.Combine( "One/.PublicStack" ) ).ShouldBeTrue();
        Directory.Exists( target.CurrentDirectory.Combine( "WithIssues/.PublicStack" ) ).ShouldBeTrue( "Reached through One." );
        Directory.EnumerateDirectories( target.CurrentDirectory )
                 .Count()
                 .ShouldBe( 3, "The cycle back to CKt cloned nothing more." );

        static void ArrangeReference( CKliEnv context, Uri stackUri, string name, string reference )
        {
            var path = context.CurrentDirectory.Combine( "Arrange" ).AppendPart( name );
            using var git = GitRepository.Clone( TestHelper.Monitor,
                                                 new GitRepositoryKey( context.SecretsStore, stackUri, isPublic: true ),
                                                 context.Committer,
                                                 path,
                                                 path.LastPart ).ShouldNotBeNull();
            SetReferences( git.WorkingFolder.AppendPart( $"{name}.xml" ), reference );
            git.Commit( TestHelper.Monitor, "Reference update." ).ShouldBe( CommitResult.Committed );
            git.PushBranch( TestHelper.Monitor, git.Repository.Head, autoCreateRemoteBranch: true ).ShouldBeTrue();
        }
    }

    static IReadOnlyList<XElement> ReadReferences( CKliEnv context )
    {
        using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                                         .ShouldNotBeNull();
        return stack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor ).ShouldNotBeNull().References;
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
