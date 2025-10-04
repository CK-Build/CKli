using NUnit.Framework;
using Shouldly;
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
        var context = ClonedPaths.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "CKt" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd CKt
        context = context.With( "CKt" );

        File.Exists( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue( "CKt-Core is in the stack." );
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet." );

        // ckli repo add file:///.../CKt-ActivityMonitor
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "add", remotes.GetUriFor( "CKt-ActivityMonitor" ) )).ShouldBeTrue();
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );

        // Moves CKt-Core at the root and renamed to BadFolderRepository.
        // Deletes CKt-ActivityMonitor.
        ClonedPaths.MoveFolder( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core" ), context.CurrentDirectory.Combine( "BadFolderRepository" ) );
        ClonedPaths.DeleteClonedFolderOnly( context.CurrentDirectory.Combine( "CKt-ActivityMonitor" ) ).ShouldBeTrue();
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
        var context = ClonedPaths.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "CKt" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd CKt
        context = context.With( "CKt" );

        File.Exists( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue( "CKt-Core is in the stack." );
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet." );

        // ckli repo add file:///.../CKt-ActivityMonitor
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "add", remotes.GetUriFor( "CKt-ActivityMonitor" ) )).ShouldBeTrue();
        File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );

        // Moves CKt-Core and CKt-ActivityMonitor to NewCore.
        Directory.CreateDirectory( context.CurrentDirectory.AppendPart( "NewCore" ) );
        ClonedPaths.MoveFolder( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core" ), context.CurrentDirectory.Combine( "NewCore/CKt-Core" ) );
        ClonedPaths.MoveFolder( context.CurrentDirectory.Combine( "CKt-ActivityMonitor" ), context.CurrentDirectory.Combine( "NewCore/CKt-ActivityMonitor" ) );

        // ckli layout xif
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "layout", "xif" )).ShouldBeTrue();

        var xml = XElement.Load( context.CurrentStackPath.AppendPart( "CKt.xml" ) );
        var onlyOneFolder = xml.Elements( "Folder" ).ShouldHaveSingleItem();
        onlyOneFolder.Attribute( "Name" ).ShouldNotBeNull().Value.ShouldBe( "NewCore" );
        onlyOneFolder.Elements().Elements().ShouldAllBe( e => e.Name.LocalName == "Repository" );
        onlyOneFolder.Elements().Attributes( "Url" ).Select( a => a.Value )
            .ToArray().ShouldBe( [ "CKt-ActivityMonitor", "CKt-Core" ], ignoreOrder: true );
    }
}
