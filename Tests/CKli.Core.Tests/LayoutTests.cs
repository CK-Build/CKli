using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class LayoutTests
{
    [Test]
    public void layout_fix()
    {
        var localPath = ClonedPaths.EnsureCleanFolder();
        var secretsStore = new DotNetUserSecretsStore();
        var remotes = Remotes.UseReadOnly( "CKt" );

        // ckli clone file:///.../CKt-Stack
        CKliCommands.Clone( TestHelper.Monitor, secretsStore, localPath, remotes.StackUri ).ShouldBe( 0 );
        // cd CKt
        localPath = localPath.AppendPart( "CKt" );

        File.Exists( localPath.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue( "CKt-Core is in the stack." );
        File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet." );


        // ckli repo add file:///.../CKt-ActivityMonitor
        CKliCommands.RepositoryAdd( TestHelper.Monitor, secretsStore, localPath, remotes.GetUriFor( "CKt-ActivityMonitor" ) ).ShouldBe( 0 );
        File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );

        // Moves CKt-Core at the root and renamed to BadFolderRepository.
        // Deletes CKt-ActivityMonitor.
        ClonedPaths.MoveFolder( localPath.Combine( "CK-Core-Projects/CKt-Core" ), localPath.Combine( "BadFolderRepository" ) );
        ClonedPaths.DeleteClonedFolderOnly( localPath.Combine( "CKt-ActivityMonitor" ) ).ShouldBeTrue();
        File.Exists( localPath.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeFalse( "Moved." );
        File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "Deleted." );

        // ckli layout fix
        CKliCommands.LayoutFix( TestHelper.Monitor, secretsStore, localPath ).ShouldBe( 0 );
        File.Exists( localPath.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue( "Back." );
        File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Back." );
        Directory.Exists( localPath.Combine( "BadFolderRepository" ) ).ShouldBeFalse();
    }

    [Test]
    public void layout_xif()
    {
        var localPath = ClonedPaths.EnsureCleanFolder();
        var secretsStore = new DotNetUserSecretsStore();
        var remotes = Remotes.UseReadOnly( "CKt" );

        // ckli clone file:///.../CKt-Stack
        CKliCommands.Clone( TestHelper.Monitor, secretsStore, localPath, remotes.StackUri ).ShouldBe( 0 );
        // cd CKt
        localPath = localPath.AppendPart( "CKt" );

        File.Exists( localPath.Combine( "CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue( "CKt-Core is in the stack." );
        File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet." );

        // ckli repo add file:///.../CKt-ActivityMonitor
        CKliCommands.RepositoryAdd( TestHelper.Monitor, secretsStore, localPath, remotes.GetUriFor( "CKt-ActivityMonitor" ) ).ShouldBe( 0 );
        File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );

        // Moves CKt-Core and CKt-ActivityMonitor to NewCore.
        Directory.CreateDirectory( localPath.AppendPart( "NewCore" ) );
        ClonedPaths.MoveFolder( localPath.Combine( "CK-Core-Projects/CKt-Core" ), localPath.Combine( "NewCore/CKt-Core" ) );
        ClonedPaths.MoveFolder( localPath.Combine( "CKt-ActivityMonitor" ), localPath.Combine( "NewCore/CKt-ActivityMonitor" ) );

        // ckli layout xif
        CKliCommands.LayoutXif( TestHelper.Monitor, secretsStore, localPath ).ShouldBe( 0 );

        var xml = XElement.Load( localPath.Combine( ".PublicStack/CKt.xml" ) );
        var onlyOneFolder = xml.Elements( "Folder" ).ShouldHaveSingleItem();
        onlyOneFolder.Attribute( "Name" ).ShouldNotBeNull().Value.ShouldBe( "NewCore" );
        onlyOneFolder.Elements().Elements().ShouldAllBe( e => e.Name.LocalName == "Repository" );
        onlyOneFolder.Elements().Attributes( "Url" ).Select( a => a.Value ).ToArray().ShouldBe(
            [
                remotes.GetUriFor( "CKt-ActivityMonitor" ).ToString(),
                remotes.GetUriFor( "CKt-Core" ).ToString()
            ], ignoreOrder: true );
    }
}
