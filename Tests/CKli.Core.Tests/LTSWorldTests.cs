using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// A Stack has one default World and any number of Long Term Support Worlds, each one defined by a
/// "@ltsName/StackName@ltsName.xml" file in the Stack repository and rooted in its own "@ltsName/" folder.
/// <para>
/// "ckli clone --lts-name" clones a Stack at one of its LTS worlds and "ckli world lts clone" obtains one in
/// an already cloned Stack.
/// </para>
/// </summary>
[TestFixture]
public class LTSWorldTests
{
    // The arranges push the LTS world definition file to the Stack remote.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [Test]
    public async Task clone_lts_name_clones_the_LTS_world_repositories_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8" );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri, "--lts-name", "@net8" ))
            .ShouldBeTrue();

        Directory.Exists( target.CurrentDirectory.Combine( "One/@net8/OneRepo" ) )
                 .ShouldBeTrue( "The LTS world's repositories are cloned in its own folder." );
        Directory.Exists( target.CurrentDirectory.Combine( "One/OneRepo" ) )
                 .ShouldBeFalse( "The default world is not the one that has been cloned." );
    }

    [Test]
    public async Task clone_lts_name_must_be_a_valid_LTS_name_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri, "--lts-name", "Net8" ))
                .ShouldBeFalse( "A LTS name starts with '@' and is lower case." );
            logs.ShouldContain( l => l.Contains( "Invalid --lts-name 'Net8'." ) );
        }
        Directory.Exists( context.CurrentDirectory.AppendPart( "One" ) ).ShouldBeFalse( "Nothing has been cloned." );
    }

    [Test]
    public async Task lts_clone_obtains_a_world_in_an_already_cloned_Stack_and_is_idempotent_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8" );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        // The default world is cloned: the "@net8" world exists in the Stack repository but has no folder.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri )).ShouldBeTrue();
        var stackRoot = target.CurrentDirectory.AppendPart( "One" );
        Directory.Exists( stackRoot.AppendPart( "@net8" ) ).ShouldBeFalse();

        var inStack = target.ChangeDirectory( stackRoot );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "world", "lts", "clone", "@net8" )).ShouldBeTrue();
        Directory.Exists( stackRoot.Combine( "@net8/OneRepo" ) ).ShouldBeTrue();
        Directory.Exists( stackRoot.AppendPart( "OneRepo" ) ).ShouldBeTrue( "The default world is untouched." );

        // Opening a world creates its plugin solution in the Stack repository: it must have been committed.
        using( var git = new LibGit2Sharp.Repository( stackRoot.AppendPart( ".PublicStack" ) ) )
        {
            git.RetrieveStatus( new LibGit2Sharp.StatusOptions() ).IsDirty
               .ShouldBeFalse( "The Stack repository has been committed." );
            git.Index.Select( e => e.Path )
               .ShouldContain( $"@net8/One-Plugins@net8/One-Plugins@net8.slnx",
                               customMessage: "The LTS world's plugin solution is tracked." );
            git.Index.Select( e => e.Path )
               .ShouldContain( $"@net8/One@net8.xml",
                               customMessage: "The LTS world's definition file is in its own folder." );
        }
        // The LTS world's plugins are compiled where they are loaded from: its own "$Local/@net8/" folder.
        var stackFolder = stackRoot.AppendPart( ".PublicStack" );
        File.Exists( stackFolder.Combine( "$Local/@net8/One-Plugins@net8/bin/CKli.Plugins/run/CKli.Plugins.dll" ) )
            .ShouldBeTrue( "The plugins have been compiled in the LTS world's local folder." );
        Directory.Exists( stackFolder.Combine( "@net8/$Local" ) )
                 .ShouldBeFalse( "Nothing is compiled in the LTS world's shared folder." );

        // Idempotent: nothing left to do.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "world", "lts", "clone", "@net8" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "has no repository to clone" ) );
        }
    }

    [Test]
    public async Task lts_clone_requires_an_existing_world_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri )).ShouldBeTrue();
        var inStack = context.ChangeDirectory( "One" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "world", "lts", "clone", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Stack 'One' has no '@net8' Long Term Support world." ) );
        }
    }

    /// <summary>
    /// "ckli world lts create" snapshots the default world's plugin solution in the new world's folder: the plugins
    /// used during the life of a LTS world are the ones it has been created with. The git ignored files are not
    /// copied (they are regenerated), the ".slnx" is renamed and the build output is redirected to the LTS world's
    /// local folder. Everything is committed in a single commit.
    /// </summary>
    [Test]
    public async Task lts_create_snapshots_the_plugin_solution_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri )).ShouldBeTrue();
        var inStack = context.ChangeDirectory( "One" );
        var stackFolder = inStack.CurrentDirectory.AppendPart( ".PublicStack" );
        var defaultSolution = stackFolder.AppendPart( "One-Plugins" );
        // Opening the default world with its plugins creates its plugin solution.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "plugin", "info" )).ShouldBeTrue();
        File.Exists( defaultSolution.AppendPart( "One-Plugins.slnx" ) ).ShouldBeTrue();
        File.WriteAllText( defaultSolution.AppendPart( "Marker.txt" ), "Snapshot me." );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "world", "lts", "create", "@net8" )).ShouldBeTrue();

        var ltsSolution = stackFolder.Combine( "@net8/One-Plugins@net8" );
        File.Exists( ltsSolution.AppendPart( "One-Plugins@net8.slnx" ) ).ShouldBeTrue( "The solution file is renamed." );
        File.Exists( ltsSolution.AppendPart( "One-Plugins.slnx" ) ).ShouldBeFalse();
        File.ReadAllText( ltsSolution.AppendPart( "Marker.txt" ) ).ShouldBe( "Snapshot me." );
        File.ReadAllText( ltsSolution.AppendPart( "Directory.Build.props" ) )
            .ShouldContain( "<ArtifactsPath>$(MSBuildThisFileDirectory)../../$Local/@net8/One-Plugins@net8</ArtifactsPath>" );
        Directory.Exists( stackFolder.Combine( "$Local/@net8" ) ).ShouldBeTrue( "The LTS world's local folder is created." );

        using( var git = new LibGit2Sharp.Repository( stackFolder ) )
        {
            git.RetrieveStatus( new LibGit2Sharp.StatusOptions() ).IsDirty
               .ShouldBeFalse( "The Stack repository has been committed." );
            git.Head.Tip.MessageShort.ShouldBe( "Created Long Term Support world 'One@net8'." );
            git.Index.Select( e => e.Path )
               .ShouldContain( "@net8/One-Plugins@net8/Marker.txt" );
            git.Index.Select( e => e.Path )
               .ShouldNotContain( $"@net8/One-Plugins@net8/{PluginMachinery.CKliVersionPropsFileName}",
                                  "A git ignored file is not copied: it is regenerated when the LTS world is opened." );
            git.Head.TrackedBranch.ShouldNotBeNull().Tip.ShouldBe( git.Head.Tip, "The Stack repository has been pushed." );
        }

        // The command has cloned the new world: opening it has compiled its snapshot in its own local folder.
        Directory.Exists( inStack.CurrentDirectory.Combine( "@net8/OneRepo" ) ).ShouldBeTrue();
        File.Exists( stackFolder.Combine( "$Local/@net8/One-Plugins@net8/bin/CKli.Plugins/run/CKli.Plugins.dll" ) ).ShouldBeTrue();
    }

    /// <summary>
    /// Cloning a LTS world installs its pinned CKli as a local tool in the world's root folder: "dotnet ckli" runs it
    /// there. Under a test harness nothing is installed (this would download the tool) but the commands are logged.
    /// </summary>
    [Test]
    public async Task lts_clone_installs_the_pinned_CKli_as_a_local_tool_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        // A locally compiled CKli ignores the pin (with a warning), a released one must match it to open the world.
        var pin = World.CKliVersion.Version == SVersion.ZeroVersion ? "1.2.3" : World.CKliVersion.Version.ToString();
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8", pin );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri, "--lts-name", "@net8" ))
                .ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "Test run: skipping 'dotnet new tool-manifest' and " )
                                     && l.Contains( $"'dotnet tool update CKli --version {pin} --allow-downgrade --source https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json'" )
                                     && l.Contains( target.CurrentDirectory.Combine( "One/@net8" ) ) );
        }
    }

    /// <summary>
    /// "ckli update" updates the global tool. In a LTS world, CKli is the pinned local tool that "dotnet ckli" runs:
    /// the version to move to must be explicit.
    /// </summary>
    [Test]
    public async Task update_in_a_LTS_world_requires_a_version_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8" );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri, "--lts-name", "@net8" ))
            .ShouldBeTrue();
        var inLTS = target.ChangeDirectory( target.CurrentDirectory.Combine( "One/@net8/OneRepo" ) );
        inLTS.LTSName.ShouldBe( "@net8" );
        target.ChangeDirectory( "One" ).LTSName.ShouldBeNull();
        target.ChangeDirectory( target.CurrentDirectory.Combine( "One/.PublicStack/@net8" ) ).LTSName
              .ShouldBeNull( "The '@net8/' folder of the Stack repository is not the LTS world's folder." );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inLTS, "update" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "The --version option is required to update the CKli version in the Long Term Support world '@net8'." ) );
        }
    }

    /// <summary>
    /// "ckli update --version" in a LTS world moves it to another CKli: its local tool, its CKliVersion pin
    /// (committed and pushed) and the CKli.Version.props of its plugin solution (deleted: its regeneration recompiles the
    /// plugins). Under a test harness, the tool commands are only logged.
    /// </summary>
    [Test]
    public async Task update_in_a_LTS_world_updates_its_pinned_CKli_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        var pin = World.CKliVersion.Version == SVersion.ZeroVersion ? "1.2.3" : World.CKliVersion.Version.ToString();
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8", pin );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri, "--lts-name", "@net8" ))
            .ShouldBeTrue();
        var stackFolder = target.CurrentDirectory.Combine( "One/.PublicStack" );
        var props = stackFolder.Combine( $"@net8/One-Plugins@net8/{PluginMachinery.CKliVersionPropsFileName}" );
        Directory.CreateDirectory( props.RemoveLastPart() );
        File.WriteAllText( props, "<Project />" );

        var inLTS = target.ChangeDirectory( target.CurrentDirectory.Combine( "One/@net8/OneRepo" ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inLTS, "update", "--version", "0.99.0", "--allow-downgrade" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( $"Updating the CKli version of the Long Term Support world 'One@net8' from '{pin}' to '0.99.0'" )
                                     && l.Contains( "on error, fix the cause and run this command again" ) );
            logs.ShouldContain( l => l.Contains( "'dotnet tool update CKli --version 0.99.0 --allow-downgrade --source https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json'" )
                                     && l.Contains( target.CurrentDirectory.Combine( "One/@net8" ) ) );
        }
        System.Xml.Linq.XDocument.Load( stackFolder.Combine( "@net8/One@net8.xml" ) ).Root.ShouldNotBeNull()
              .Attribute( XNames.CKliVersion ).ShouldNotBeNull().Value.ShouldBe( "0.99.0" );
        File.Exists( props ).ShouldBeFalse( "Its regeneration recompiles the plugins against the new CKli." );
        using( var git = new LibGit2Sharp.Repository( stackFolder ) )
        {
            git.Head.Tip.MessageShort.ShouldBe( "Updated CKli version of 'One@net8' to '0.99.0'." );
            git.RetrieveStatus( new LibGit2Sharp.StatusOptions() ).IsDirty.ShouldBeFalse();
            git.Head.TrackedBranch.ShouldNotBeNull().Tip.ShouldBe( git.Head.Tip, "The new pin has been pushed." );
        }
    }

    /// <summary>
    /// A LTS world is defined by the "@ltsName/StackName@ltsName.xml" file: the same file at the root of the
    /// Stack repository (next to the default world's one) defines nothing.
    /// </summary>
    [Test]
    public async Task a_LTS_world_definition_file_must_be_in_the_LTS_folder_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri )).ShouldBeTrue();
        var inStack = context.ChangeDirectory( "One" );
        File.WriteAllText( inStack.CurrentDirectory.Combine( ".PublicStack/One@net8.xml" ),
                           """
                           <One LTSName="@net8">
                             <Repository Url="OneRepo" />
                           </One>
                           """ );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "world", "lts", "clone", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Stack 'One' has no '@net8' Long Term Support world." ) );
        }
    }

    /// <summary>
    /// The default world's layout must ignore the LTS worlds' folders. Without this, its repositories look
    /// misplaced: "layout fix" moves them out of the LTS world (and "layout xif" adopts them).
    /// </summary>
    [Test]
    public async Task the_default_world_layout_ignores_the_LTS_world_folders_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8" );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri, "--lts-name", "@net8" ))
            .ShouldBeTrue();
        var stackRoot = target.CurrentDirectory.AppendPart( "One" );
        var inDefaultWorld = target.ChangeDirectory( stackRoot );

        // The default world has no repository cloned and the "@net8" world has OneRepo.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inDefaultWorld, "layout", "fix" )).ShouldBeTrue();

        Directory.Exists( stackRoot.Combine( "@net8/OneRepo" ) )
                 .ShouldBeTrue( "The LTS world's repository has not been moved out of it." );
        Directory.Exists( stackRoot.AppendPart( "OneRepo" ) )
                 .ShouldBeTrue( "The default world cloned its own copy." );
    }

    /// <summary>
    /// Pushes a "@{ltsName}/{name}@{ltsName}.xml" world definition file to the Stack remote. Its layout is the same
    /// single repository as the fixture's default world.
    /// </summary>
    static void ArrangeLTSWorld( CKliEnv context, Uri stackUri, string name, string ltsName, string? ckliVersion = null )
    {
        var path = context.CurrentDirectory.Combine( "Arrange" ).AppendPart( name );
        using var git = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, stackUri, isPublic: true ),
                                             context.Committer,
                                             path,
                                             path.LastPart ).ShouldNotBeNull();
        // An LTS world definition file must carry its LTSName on its root element.
        var ltsFolder = git.WorkingFolder.AppendPart( ltsName );
        Directory.CreateDirectory( ltsFolder );
        File.WriteAllText( ltsFolder.AppendPart( $"{name}{ltsName}.xml" ),
                           $"""
                            <{name} LTSName="{ltsName}"{(ckliVersion != null ? $" CKliVersion=\"{ckliVersion}\"" : "")}>
                              <Repository Url="OneRepo" />
                            </{name}>
                            """ );
        git.Commit( TestHelper.Monitor, $"Added '{ltsName}' world." ).ShouldBe( CommitResult.Committed );
        git.PushBranch( TestHelper.Monitor, git.Repository.Head, autoCreateRemoteBranch: true ).ShouldBeTrue();
    }
}
