using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class LayoutTests
{
    [Test]
    public async Task layout_fix_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd CKt
        context = context.ChangeDirectory( "CKt" );

        File.Exists( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue( "CKt-Core is in the stack." );
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet." );

        // ckli repo add file:///.../CKt-ActivityMonitor -?
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "add", remotes.GetUriFor( "CKt-ActivityMonitor" ), "-?" )).ShouldBeTrue();
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet, Just the help." );


        // ckli repo add file:///.../CKt-ActivityMonitor
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "add", remotes.GetUriFor( "CKt-ActivityMonitor" ) )).ShouldBeTrue();
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );

        // Moves CKt-Core at the root and renamed to BadFolderRepository.
        // Deletes CKt-ActivityMonitor.
        TestEnv.MoveFolder( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core" ), context.CurrentDirectory.Combine( "BadFolderRepository" ) );
        TestEnv.DeleteFolder( context.CurrentDirectory.Combine( "CKt-ActivityMonitor" ) );
        File.Exists( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeFalse( "Moved." );
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "Deleted." );

        // ckli layout fix
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "layout", "fix" )).ShouldBeTrue();
        File.Exists( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue( "Back." );
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Back." );
        Directory.Exists( context.CurrentDirectory.Combine( "BadFolderRepository" ) ).ShouldBeFalse();
    }

    [Test]
    public async Task layout_xif_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd CKt
        context = context.ChangeDirectory( "CKt" );

        File.Exists( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue( "CKt-Core is in the stack." );
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet." );

        // ckli repo add file:///.../CKt-ActivityMonitor
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "add", remotes.GetUriFor( "CKt-ActivityMonitor" ) )).ShouldBeTrue();
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );

        // Moves CKt-Core and CKt-ActivityMonitor to NewCore.
        Directory.CreateDirectory( context.CurrentDirectory.AppendPart( "NewCore" ) );
        TestEnv.MoveFolder( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core" ), context.CurrentDirectory.Combine( "NewCore/CKt-Core" ) );
        TestEnv.MoveFolder( context.CurrentDirectory.Combine( "CKt-ActivityMonitor" ), context.CurrentDirectory.Combine( "NewCore/CKt-ActivityMonitor" ) );

        // ckli layout xif
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "layout", "xif" )).ShouldBeTrue();

        var xml = XElement.Load( context.CurrentStackPath.AppendPart( "CKt.xml" ) );
        var onlyOneFolder = xml.Elements( "Folder" ).ShouldHaveSingleItem();
        onlyOneFolder.Attribute( "Name" ).ShouldNotBeNull().Value.ShouldBe( "NewCore" );
        onlyOneFolder.Elements().Elements().ShouldAllBe( e => e.Name.LocalName == "Repository" || e.Name.LocalName == "SomePlugin" );
        onlyOneFolder.Elements().Attributes( "Url" ).Select( a => a.Value )
            .ToArray().ShouldBe( [ "CKt-ActivityMonitor", "CKt-Core" ], ignoreOrder: true );

        XElement cktCore = onlyOneFolder.Elements().Single( e => e.Attributes( "Url" ).Single().Value == "CKt-Core" );
        cktCore.HasElements.ShouldBeTrue();
        cktCore.Element( "SomePlugin" ).ShouldNotBeNull().ToString().ShouldBe( "<SomePlugin PerRepoPluginConfiguration=\"comes here\" />" );

    }

    [Test]
    public async Task layout_xif_new_project_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );
        var display = (StringScreen)context.Screen;

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd CKt
        context = context.ChangeDirectory( "CKt" );

        string tempPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath(), "xif-layout", DateTime.UtcNow );
        try
        {
            var hostingProvider = GitHosting.FileSystemProviderTests.GetFileHostingProvider();
            var remotePath = new NormalizedPath( tempPath ).AppendPart( "Remote" ).AppendPart( "CK-H3" );
            var info = await hostingProvider.CreateRepositoryAsync( TestHelper.Monitor, remotePath, defaultBranchName: "stable" );
            info.ShouldNotBeNull();
            info.CloneUrl.ShouldNotBeNull().ShouldBe( "file://" + remotePath );

            // We clone the new repo with a "CK-H3/.git" remote url. The Xif layout will "correct" this by removing the ".git" extension.
            var uri = new Uri( info.CloneUrl /*+ "/.git"*/ );
            var clonedPath = new NormalizedPath( tempPath ).AppendPart( "Cloned" ).AppendPart( "CK-H3" );
            using( var cloned = new Repository( Repository.Clone( uri.ToString(), clonedPath ) ) )
            {
                File.WriteAllText( clonedPath.AppendPart( "CK-H3.slnx" ), "<Solution></Solution>" );
                Commands.Stage( cloned, "*" );
                var signature = new Signature( "test", "(none)", DateTimeOffset.Now );
                cloned.Commit( "Initialization.", signature, signature );
                cloned.Network.Push( cloned.Branches["stable"] );
            }
            // Moves the cloned folder in Misc/ folder.
            var misc = context.CurrentDirectory.AppendPart( "Misc" );
            FileHelper.MoveFolder( TestHelper.Monitor, clonedPath, misc.AppendPart( "CK-H3" ) ).ShouldBeTrue();

            // ckli layout xif
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "layout", "xif" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            ❰✓❱

            """ );

            XElement.Load( context.CurrentDirectory.AppendPart( ".PublicStack" ).AppendPart( "CKt.xml" ) )
                .Element("CKt")?
                    .Elements("Folder").Single( f => f.Attribute("Name")?.Value == "Misc" )
                        .Elements().Single()
                        .ShouldMatch( r => r.Name.LocalName == "Repository" );
        }
        finally
        {
            FileHelper.DeleteFolder( TestHelper.Monitor, tempPath );
        }

    }
}
