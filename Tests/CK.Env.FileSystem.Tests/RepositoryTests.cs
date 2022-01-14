using CK.Core;
using CK.SimpleKeyVault;

using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.FS.Tests
{
    [TestFixture]
    public class RepositoryTests
    {

        readonly CommandRegister _commandRegister = new CommandRegister();
        readonly SecretKeyStore _keyStore = new SecretKeyStore();


        [Test]
        public void FileSystem_sees_physical_files()
        {
            _commandRegister.UnregisterAll();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                fs.GetDirectoryContents( "" ).Select( f => $"{f.Name} - {f.IsDirectory}" )
                .Should().Contain( "Test.xml - False" )
                        .And.Contain( "TestGitRepository - True" )
                        .And.Contain( "SubDir - True" )
                        .And.Contain( "EmptyDir - True" );
            }
        }

        [Test]
        public void FileSystem_sees_Git_repo_with_a_null_PhysicalPath()
        {
            _commandRegister.UnregisterAll();
            var w = new WorldMock();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var proto = fs.FindOrCreateProtoGitFolder( TestHelper.Monitor, w, "TestGitRepository", LocalTestHelper.TestGitRepositoryUrl );
                fs.EnsureGitFolder( TestHelper.Monitor, proto ).Should().NotBeNull();
                CheckRoot( fs.GetFileInfo( "TestGitRepository" ) );
                CheckRoot( fs.GetFileInfo( "/TestGitRepository" ) );
                CheckRoot( fs.GetFileInfo( "TestGitRepository/" ) );
                CheckRoot( fs.GetFileInfo( "/TestGitRepository/" ) );
                CheckRoot( fs.GetFileInfo( "\\TestGitRepository" ) );
                CheckRoot( fs.GetFileInfo( "TestGitRepository\\" ) );
                CheckRoot( fs.GetFileInfo( "\\TestGitRepository\\" ) );

                static void CheckRoot( IFileInfo r )
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
            _commandRegister.UnregisterAll();
            var w = new WorldMock();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var proto = fs.FindOrCreateProtoGitFolder( TestHelper.Monitor, w, "TestGitRepository", LocalTestHelper.TestGitRepositoryUrl );
                fs.EnsureGitFolder( TestHelper.Monitor, proto );
                CheckRootContent( fs.GetDirectoryContents( "TestGitRepository" ) );
                CheckRootContent( fs.GetDirectoryContents( "/TestGitRepository" ) );
                CheckRootContent( fs.GetDirectoryContents( "TestGitRepository/" ) );
                CheckRootContent( fs.GetDirectoryContents( "/TestGitRepository/" ) );
                CheckRootContent( fs.GetDirectoryContents( "\\TestGitRepository" ) );
                CheckRootContent( fs.GetDirectoryContents( "TestGitRepository\\" ) );
                CheckRootContent( fs.GetDirectoryContents( "\\TestGitRepository\\" ) );

                static void CheckRootContent( IDirectoryContents c )
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
            _commandRegister.UnregisterAll();
            var w = new WorldMock();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var proto = fs.FindOrCreateProtoGitFolder( TestHelper.Monitor, w, "TestGitRepository", LocalTestHelper.TestGitRepositoryUrl );
                fs.EnsureGitFolder( TestHelper.Monitor, proto );
                CheckHead( fs.GetFileInfo( "TestGitRepository/head" ) );
                CheckHead( fs.GetFileInfo( "/TestGitRepository/head" ) );
                CheckHead( fs.GetFileInfo( "TestGitRepository/head/" ) );
                CheckHead( fs.GetFileInfo( "/TestGitRepository/head/" ) );
                CheckHead( fs.GetFileInfo( "\\TestGitRepository\\head" ) );
                CheckHead( fs.GetFileInfo( "TestGitRepository\\head\\" ) );
                CheckHead( fs.GetFileInfo( "\\TestGitRepository\\head\\" ) );

                static void CheckHead( IFileInfo r )
                {
                    r.IsDirectory.Should().BeTrue();
                    r.Name.Should().Be( "head" );
                    r.PhysicalPath.Should().Be( LocalTestHelper.WorldFolder.AppendPart( "TestGitRepository" ) );
                }

                CheckHeadContent( fs.GetDirectoryContents( "TestGitRepository/head" ) );
                CheckHeadContent( fs.GetDirectoryContents( "/TestGitRepository/head" ) );
                CheckHeadContent( fs.GetDirectoryContents( "TestGitRepository/head/" ) );
                CheckHeadContent( fs.GetDirectoryContents( "/TestGitRepository/head/" ) );
                CheckHeadContent( fs.GetDirectoryContents( "\\TestGitRepository\\head" ) );
                CheckHeadContent( fs.GetDirectoryContents( "TestGitRepository\\head\\" ) );
                CheckHeadContent( fs.GetDirectoryContents( "\\TestGitRepository\\head\\" ) );

                static void CheckHeadContent( IDirectoryContents c )
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
            _commandRegister.UnregisterAll();
            var w = new WorldMock();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var proto = fs.FindOrCreateProtoGitFolder( TestHelper.Monitor, w, "TestGitRepository", LocalTestHelper.TestGitRepositoryUrl );
                fs.EnsureGitFolder( TestHelper.Monitor, proto );
                var f = fs.GetFileInfo( "TestGitRepository/branches" );
                f.IsDirectory.Should().BeTrue();
                f.Name.Should().Be( "branches" );
                f.PhysicalPath.Should().BeNull();

                var c = fs.GetDirectoryContents( "/TestGitRepository/branches/" );
                c.Exists.Should().BeTrue();
                c.Should().OnlyContain( d => d.IsDirectory );
                c.Select( d => d.Name )
                    .Should().Contain( IWorldName.MasterName );
            }
        }

        [Test]
        public void GitFolder_CheckCleanCommit_detects_new_deleted_and_modified_files()
        {
            Assume.That( TestHelper.IsExplicitAllowed );
            _commandRegister.UnregisterAll();
            var w = new WorldMock();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var proto = fs.FindOrCreateProtoGitFolder( TestHelper.Monitor, w, "TestGitRepository", LocalTestHelper.TestGitRepositoryUrl );
                var git = fs.EnsureGitFolder( TestHelper.Monitor, proto );
                git.CheckCleanCommit( TestHelper.Monitor ).Should().BeTrue();
                git.CurrentBranchName.Should().Be( IWorldName.MasterName );

                var gitRoot = git.SubPath.AppendPart( "branches" ).AppendPart( IWorldName.MasterName );

                // Detecting New file.
                fs.CopyTo( TestHelper.Monitor, "newFile", gitRoot.AppendPart( "new.txt" ) );
                git.CheckCleanCommit( TestHelper.Monitor ).Should().BeFalse();
                // Restoring 
                fs.Delete( TestHelper.Monitor, gitRoot.AppendPart( "new.txt" ) );
                git.CheckCleanCommit( TestHelper.Monitor ).Should().BeTrue();

                var savedMasterContent = fs.GetFileInfo( gitRoot.AppendPart( "master.txt" ) ).AsTextFileInfo().TextContent;
                // Detecting Deleted file.
                fs.Delete( TestHelper.Monitor, gitRoot.AppendPart( "master.txt" ) );
                git.CheckCleanCommit( TestHelper.Monitor ).Should().BeFalse();
                // Restoring 
                fs.CopyTo( TestHelper.Monitor, savedMasterContent, gitRoot.AppendPart( "master.txt" ) );
                git.CheckCleanCommit( TestHelper.Monitor ).Should().BeTrue();

                // Detecting Modified file.
                fs.CopyTo( TestHelper.Monitor, "paf!", gitRoot.AppendPart( "master.txt" ) );
                git.CheckCleanCommit( TestHelper.Monitor ).Should().BeFalse();
                // Restoring 
                fs.CopyTo( TestHelper.Monitor, savedMasterContent, gitRoot.AppendPart( "master.txt" ) );
                git.CheckCleanCommit( TestHelper.Monitor ).Should().BeTrue();
            }
        }

        [Test]
        public void FileSystem_remotes_contains_the_remote_branches()
        {
            _commandRegister.UnregisterAll();
            var w = new WorldMock();
            SecretKeyStore keyStore = new SecretKeyStore();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, keyStore, new SimpleServiceContainer() ) )
            {
                fs.ServiceContainer.Add( keyStore );
                var proto = fs.FindOrCreateProtoGitFolder( TestHelper.Monitor, w, "TestGitRepository", LocalTestHelper.TestGitRepositoryUrl );
                fs.EnsureGitFolder( TestHelper.Monitor, proto );

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
                    .Should().Contain( new[] { IWorldName.MasterName } );
            }
        }

        /// <summary>
        /// Issue; https://github.com/aspnet/AspNetCore/issues/2891
        /// </summary>
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
        public void when_the_branch_is_the_current_one_we_have_access_to_the_physical_files()
        {
            Assume.That( TestHelper.IsExplicitAllowed );
            _commandRegister.UnregisterAll();
            var w = new WorldMock();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var proto = fs.FindOrCreateProtoGitFolder( TestHelper.Monitor, w, "TestGitRepository", LocalTestHelper.TestGitRepositoryUrl );
                var git = fs.EnsureGitFolder( TestHelper.Monitor, proto );
                fs.GitFolders[0].CurrentBranchName.Should().Be( IWorldName.MasterName, "The TestRepository must be on IWorldName.MasterName." );

                var fA = fs.GetFileInfo( $"TestGitRepository/branches/{IWorldName.MasterName}/a" );
                fA.IsDirectory.Should().BeTrue();
                fA.Name.Should().Be( "a" );
                fA.PhysicalPath.Should().NotBeNull();
                new NormalizedPath( fA.PhysicalPath ).Should().Be( LocalTestHelper.WorldFolder.Combine( "TestGitRepository/a" ) );

                var fMaster = fs.GetFileInfo( $"TestGitRepository/branches/{IWorldName.MasterName}/master.txt" );
                fMaster.IsDirectory.Should().BeFalse();
                fMaster.Name.Should().Be( "master.txt" );
                fMaster.PhysicalPath.Should().NotBeNull();
                new NormalizedPath( fMaster.PhysicalPath ).Should().Be( LocalTestHelper.WorldFolder.Combine( "TestGitRepository/master.txt" ) );
                using( var content = fMaster.CreateReadStream() )
                using( var textR = new StreamReader( content ) )
                {
                    textR.ReadToEnd().NormalizeEOLToLF().Should().Be( $"On {IWorldName.MasterName}\nOn {IWorldName.MasterName}\n" );
                }
            }

        }

        class WorldMock : IWorldName
        {
            public string Name => "World";

            public string ParallelName => null;

            public string DevelopBranchName => IWorldName.DevelopName;

            public string MasterBranchName => IWorldName.MasterName;

            public string LocalBranchName => $"{IWorldName.DevelopName}-local";

            public string FullName => "World";
        }

        [Test]
        public void once_in_a_branch_we_have_access_to_directory_content_in_read_only_or_writable_if_in_current_head()
        {
            _commandRegister.UnregisterAll();
            var w = new WorldMock();
            using( var fs = new FileSystem( LocalTestHelper.WorldFolder, _commandRegister, _keyStore, new SimpleServiceContainer() ) )
            {
                var proto = fs.FindOrCreateProtoGitFolder( TestHelper.Monitor, w, "TestGitRepository", LocalTestHelper.TestGitRepositoryUrl );
                var git = fs.EnsureGitFolder( TestHelper.Monitor, proto );
                fs.GitFolders[0].Checkout( TestHelper.Monitor, "alpha" ).Success.Should().BeTrue( "Go to 'alpha' branch." ); ;

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

                fs.GitFolders[0].Checkout( TestHelper.Monitor, IWorldName.MasterName ).Success.Should().BeTrue( "Back to IWorldName.MasterName." );

                var cAW = fs.GetDirectoryContents( $"TestGitRepository/branches/{IWorldName.MasterName}/a" );
                cAW.Exists.Should().BeTrue();
                cAW.All( f => f.Exists ).Should().BeTrue();
                cAW.Select( f => f.Name ).Should().BeEquivalentTo( "a1", "a1.txt", "a2.txt" );

                {
                    var fA2 = fs.GetFileInfo( $"TestGitRepository/branches/{IWorldName.MasterName}/a/a2.txt" );
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
