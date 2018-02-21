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
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    [TestFixture]
    public class RepositoryTests
    {
        [Test]
        public void FileSystem_sees_physical_files()
        {
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, autoDiscoverAllGitFolders: true ) )
            {
                fs.GetDirectoryContents( "" ).Select( f => $"{f.Name} - {f.IsDirectory}" )
                .Should().Contain( "KnownWorld.xml - False" )
                        .And.Contain( "TestGitRepository - True" )
                        .And.Contain( "SubDir - True" )
                        .And.Contain( "EmptyDir - True" );
            }
        }

        [Test]
        public void FileSystem_sees_Git_repo_with_a_null_PhysicalPath()
        {
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, autoDiscoverAllGitFolders: true ) )
            {
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
        }

        [Test]
        public void FileSystem_sees_Git_repo_with_head_and_branches_subfolder()
        {
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, autoDiscoverAllGitFolders: true ) )
            {
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

        }
        [Test]
        public void FileSystem_sees_Git_head_as_the_PhysicalDirectory()
        {
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, autoDiscoverAllGitFolders: true ) )
            {
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
                    r.PhysicalPath.Should().Be( Path.Combine( LocalTestHelper.WorldFolder, "TestGitRepository" ) );
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
        }


        [Test]
        public void FileSystem_sees_the_branches_as_directories()
        {
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, autoDiscoverAllGitFolders: true ) )
            {
                var f = fs.GetFileInfo( "TestGitRepository/branches" );
                f.IsDirectory.Should().BeTrue();
                f.Name.Should().Be( "branches" );
                f.PhysicalPath.Should().BeNull();

                var c = fs.GetDirectoryContents( "/TestGitRepository/branches/" );
                c.Exists.Should().BeTrue();
                c.Should().OnlyContain( d => d.IsDirectory );
                c.Select( d => d.Name )
                    .Should().Contain( "master" );
            }
        }

        [Test]
        public void FileSystem_remotes_contains_the_remote_branches()
        {
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, autoDiscoverAllGitFolders: true ) )
            {
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
        }

        [Test]
        public void standard_physical_file_provider_is_full_of_surprises()
        {
            using( var pStd = new PhysicalFileProvider( LocalTestHelper.WorldFolder ) )
            {
                var fCA = pStd.GetDirectoryContents( "TestGitRepository" );
                fCA.Exists.Should().BeTrue( "Yes, this is a directory..." );

                var fA = pStd.GetFileInfo( "TestGitRepository" );

                fA.IsDirectory.Should().BeFalse( "What??? This IS a directory!" );
                fA.Exists.Should().BeFalse( "And it DOES exist !" );
            }
        }


        [Test]
        public void when_the_branch_is_thecurrent_one_we_have_access_to_the_physical_files()
        {
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, autoDiscoverAllGitFolders: true ) )
            {
                fs.GitFolders[0].CurrentBranchName.Should().Be( "master", "The TestRepository must be on 'master'." );

                var fA = fs.GetFileInfo( "TestGitRepository/branches/master/a" );
                fA.IsDirectory.Should().BeTrue();
                fA.Name.Should().Be( "a" );
                fA.PhysicalPath.Should().NotBeNull();
                fA.PhysicalPath.Should().Be( LocalTestHelper.WorldFolder.Combine( "TestGitRepository/a" ) );

                var fMaster = fs.GetFileInfo( "TestGitRepository/branches/master/master.txt" );
                fMaster.IsDirectory.Should().BeFalse();
                fMaster.Name.Should().Be( "master.txt" );
                fMaster.PhysicalPath.Should().NotBeNull();
                fMaster.PhysicalPath.Should().Be( LocalTestHelper.WorldFolder.Combine( "TestGitRepository/master.txt" ) );
                using( var content = fMaster.CreateReadStream() )
                using( var textR = new StreamReader( content ) )
                {
                    textR.ReadToEnd().NormalizeEOLToLF().Should().Be( "On master\nOn master\n" );
                }
            }

        }

        [Test]
        public void once_in_a_branch_we_have_access_to_directory_content_in_read_only_or_writable_if_in_current_head()
        {
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, autoDiscoverAllGitFolders: true ) )
            {

                fs.GitFolders[0].Checkout( TestHelper.Monitor, "alpha" ).Should().BeTrue( "Go to 'alpha' branch." ); ;

                var cA = fs.GetDirectoryContents( "TestGitRepository/branches/master/a" );
                cA.Exists.Should().BeTrue();
                cA.All( f => f.Exists ).Should().BeTrue();
                cA.Select( f => f.Name ).Should().BeEquivalentTo( "a1", "a1.txt", "a2.txt" );

                {
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

                fs.GitFolders[0].Checkout( TestHelper.Monitor, "master" ).Should().BeTrue( "Back to master." );

                var cAW = fs.GetDirectoryContents( "TestGitRepository/branches/master/a" );
                cAW.Exists.Should().BeTrue();
                cAW.All( f => f.Exists ).Should().BeTrue();
                cAW.Select( f => f.Name ).Should().BeEquivalentTo( "a1", "a1.txt", "a2.txt" );

                {
                    var fA2 = fs.GetFileInfo( "TestGitRepository/branches/master/a/a2.txt" );
                    fA2.IsDirectory.Should().BeFalse();
                    fA2.Name.Should().Be( "a2.txt" );
                    fA2.PhysicalPath.Should().NotBeNull( "Since we have checked out master." );
                    using( var content = fA2.CreateReadStream() )
                    using( var textR = new StreamReader( content ) )
                    {
                        textR.ReadToEnd().NormalizeEOLToLF().Should().Be( "a2\na2\n" );
                    }
                }


            }
        }


    }
}
