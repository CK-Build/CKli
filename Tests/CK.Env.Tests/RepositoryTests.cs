using CK.Core;
using CK.Text;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CK.Env.Tests
{
    [TestFixture]
    public class RepositoryTests
    {
        [Test]
        public void FileSystem_sees_physical_files()
        {
            var fs = new FileSystem( TestHelper.WorldFolder );
            fs.Root.Path.Should().NotEndWith( FileUtil.DirectorySeparatorString );
            fs.GetDirectoryContents( "" ).Select( f => $"{f.Name} - {f.IsDirectory}" )
                .Should().Contain( "KnownWorld.xml - False" )
                        .And.Contain( "TestGitRepository - True" )
                        .And.Contain( "SubDir - True" )
                        .And.Contain( "EmptyDir - True" );
        }

        [Test]
        public void FileSystem_sees_Git_repo_with_a_null_PhysicalPath()
        {
            var fs = new FileSystem( TestHelper.WorldFolder );
            CheckRoot( fs.GetFileInfo( "TestGitRepository" ) );
            CheckRoot( fs.GetFileInfo( "/TestGitRepository" ) );
            CheckRoot( fs.GetFileInfo( "TestGitRepository/" ) );
            CheckRoot( fs.GetFileInfo( "/TestGitRepository/" ) );
            CheckRoot( fs.GetFileInfo( "\\TestGitRepository" ) );
            CheckRoot( fs.GetFileInfo( "TestGitRepository\\" ) );
            CheckRoot( fs.GetFileInfo( "\\TestGitRepository\\" ) );

            void CheckRoot( IFileInfo r )
            {
                r.IsDirectory.Should().BeTrue();
                r.Name.Should().Be( "TestGitRepository" );
                r.PhysicalPath.Should().BeNull();
            }
        }

        [Test]
        public void FileSystem_sees_Git_repo_with_head_and_branches_subfolder()
        {
            var fs = new FileSystem( TestHelper.WorldFolder );
            CheckRootContent( fs.GetDirectoryContents( "TestGitRepository" ) );
            CheckRootContent( fs.GetDirectoryContents( "/TestGitRepository" ) );
            CheckRootContent( fs.GetDirectoryContents( "TestGitRepository/" ) );
            CheckRootContent( fs.GetDirectoryContents( "/TestGitRepository/" ) );
            CheckRootContent( fs.GetDirectoryContents( "\\TestGitRepository" ) );
            CheckRootContent( fs.GetDirectoryContents( "TestGitRepository\\" ) );
            CheckRootContent( fs.GetDirectoryContents( "\\TestGitRepository\\" ) );

            void CheckRootContent( IDirectoryContents c )
            {
                c.Exists.Should().BeTrue();
                c.Select( f => $"{f.Name} - {f.IsDirectory}" )
                    .Should().Contain( new[] { "head - True", "branches - True", "remotes - True" } );
            }
        }


        [Test]
        public void FileSystem_sees_Git_head_as_the_PhysicalDirectory()
        {
            var fs = new FileSystem( TestHelper.WorldFolder );
            CheckHead( fs.GetFileInfo( "TestGitRepository/head" ) );
            CheckHead( fs.GetFileInfo( "/TestGitRepository/head" ) );
            CheckHead( fs.GetFileInfo( "TestGitRepository/head/" ) );
            CheckHead( fs.GetFileInfo( "/TestGitRepository/head/" ) );
            CheckHead( fs.GetFileInfo( "\\TestGitRepository\\head" ) );
            CheckHead( fs.GetFileInfo( "TestGitRepository\\head\\" ) );
            CheckHead( fs.GetFileInfo( "\\TestGitRepository\\head\\" ) );

            void CheckHead( IFileInfo r )
            {
                r.IsDirectory.Should().BeTrue();
                r.Name.Should().Be( "head" );
                r.PhysicalPath.Should().Be( Path.Combine( TestHelper.WorldFolder, "TestGitRepository" ) );
            }

            CheckHeadContent( fs.GetDirectoryContents( "TestGitRepository/head" ) );
            CheckHeadContent( fs.GetDirectoryContents( "/TestGitRepository/head" ) );
            CheckHeadContent( fs.GetDirectoryContents( "TestGitRepository/head/" ) );
            CheckHeadContent( fs.GetDirectoryContents( "/TestGitRepository/head/" ) );
            CheckHeadContent( fs.GetDirectoryContents( "\\TestGitRepository\\head" ) );
            CheckHeadContent( fs.GetDirectoryContents( "TestGitRepository\\head\\" ) );
            CheckHeadContent( fs.GetDirectoryContents( "\\TestGitRepository\\head\\" ) );

            void CheckHeadContent( IDirectoryContents c )
            {
                c.Exists.Should().BeTrue();
                c.Select( f => $"{f.Name} - {f.IsDirectory}" )
                    .Should().Contain( "a - True" )
                             .And.Contain( "b - True" )
                             .And.Contain( "c - True" )
                             .And.Contain( "master.txt - False" );
            }

        }


        [Test]
        public void FileSystem_sees_the_branches_as_directories()
        {
            var fs = new FileSystem( TestHelper.WorldFolder );

            var f = fs.GetFileInfo( "TestGitRepository/branches" );
            f.IsDirectory.Should().BeTrue();
            f.Name.Should().Be( "branches" );
            f.PhysicalPath.Should().BeNull();

            var c = fs.GetDirectoryContents( "/TestGitRepository/branches/" );
            c.Exists.Should().BeTrue();
            c.Should().OnlyContain( d => d.IsDirectory );
            c.Select( d => d.Name )
                .Should().HaveCount( 1 ).And.Contain( "master" );
        }

        [Test]
        public void FileSystem_remotes_contains_the_remote_branches()
        {
            var fs = new FileSystem( TestHelper.WorldFolder );

            var f = fs.GetFileInfo( "TestGitRepository/remotes/" );
            f.IsDirectory.Should().BeTrue();
            f.Name.Should().Be( "remotes" );
            f.PhysicalPath.Should().BeNull();

            var c = fs.GetDirectoryContents( "/TestGitRepository/remotes" );
            c.Exists.Should().BeTrue();
            c.Should().OnlyContain( d => d.IsDirectory );
            c.Select( d => d.Name )
                .Should().Contain( new[] { "origin" } );

            var fO = fs.GetFileInfo( "/TestGitRepository/remotes/" + c.Single().Name );
            fO.IsDirectory.Should().BeTrue();
            fO.Name.Should().Be( c.Single().Name );
            fO.PhysicalPath.Should().BeNull();

            var cO = fs.GetDirectoryContents( "/TestGitRepository/remotes/" + c.Single().Name );
            cO.Exists.Should().BeTrue();
            cO.Should().OnlyContain( d => d.IsDirectory );
            cO.Select( d => d.Name )
                .Should().Contain( new[] { "master" } );
        }


        [Test]
        public void once_in_a_branch_we_have_access_to_the_files()
        {
            var fs = new FileSystem( TestHelper.WorldFolder );

            var fA = fs.GetFileInfo( "TestGitRepository/branches/master/a" );
            fA.IsDirectory.Should().BeTrue();
            fA.Name.Should().Be( "a" );
            fA.PhysicalPath.Should().BeNull();

            var fMaster = fs.GetFileInfo( "TestGitRepository/branches/master/master.txt" );
            fMaster.IsDirectory.Should().BeFalse();
            fMaster.Name.Should().Be( "master.txt" );
            fMaster.PhysicalPath.Should().BeNull();
            using( var content = fMaster.CreateReadStream() )
            using( var textR = new StreamReader( content ) )
            {
                textR.ReadToEnd().NormalizeEOLToLF().Should().Be( "On master\nOn master\n" );
            }
        }

        [Test]
        public void once_in_a_branch_we_have_access_to_directory_content()
        {
            var fs = new FileSystem( TestHelper.WorldFolder );

            var cA = fs.GetDirectoryContents( "TestGitRepository/branches/master/a" );
            cA.Exists.Should().BeTrue();
            cA.All( f => f.Exists ).Should().BeTrue();
            cA.Select( f => f.Name ).ShouldBeEquivalentTo(new[] { "a1", "a1.txt", "a2.txt" } );

            var fA2 = fs.GetFileInfo( "TestGitRepository/branches/master/a/a2.txt" );
            fA2.IsDirectory.Should().BeFalse();
            fA2.Name.Should().Be( "a2.txt" );
            fA2.PhysicalPath.Should().BeNull();
            using( var content = fA2.CreateReadStream() )
            using( var textR = new StreamReader( content ) )
            {
                textR.ReadToEnd().NormalizeEOLToLF().Should().Be( "a2\na2\n" );
            }
        }


    }
}
