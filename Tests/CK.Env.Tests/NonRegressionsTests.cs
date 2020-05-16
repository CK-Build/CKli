using CK.Env.Tests.LocalTestHelper;
using CK.Text;
using FluentAssertions;
using LibGit2Sharp;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    public class NonRegressionsTests
    {
        /// <summary>
        /// When switching to local, CKli did checkout on develop but we were on a local branch.
        /// It should had keep the local branch checkout.
        /// </summary>
        [Test]
        public void ckli_save_branch_state_on_restart()
        {
            ImageLibrary.minimal_solution_switched_to_local( ( universe, world ) =>
            {
                world.CheckGlobalGitStatus( TestHelper.Monitor, StandardGitStatus.Local ).Should().BeTrue();
                universe.UserHost.WorldSelector.CloseWorld( TestHelper.Monitor );
                var reopenedWorld = universe.EnsureWorldOpened( TestHelper.Monitor, world.WorldName.Name );
                reopenedWorld.CheckGlobalGitStatus( TestHelper.Monitor, StandardGitStatus.Local ).Should().BeTrue();
            }, TestHelper.IsExplicitAllowed );
        }

        /// <summary>
        /// https://github.com/CK-Build/CKli/issues/20
        /// </summary>
        [Test]
        public void issue_20()
        {
            ImageLibrary.minimal_solution_first_ci_build( ( universe, world ) =>
            {
                var monitor = TestHelper.Monitor;

                world.GitRepositories.All( g => g.CheckCleanCommit( monitor ) ).Should().BeTrue( "All repositories should be cleaned." );

                universe
                    .RunCommands( monitor, world.WorldName.Name, "*pull*" )
                    .RunCommands( monitor, world.WorldName.Name, "*command*", "git checkout master" )
                    .RunCommands( monitor, world.WorldName.Name, "*command*", "git pull" );

                world.DumpWorldState( TestHelper.Monitor ).Should().BeTrue( "All repositories should be cleaned after the pull/checkout mster/pull." );

            }, TestHelper.IsExplicitAllowed );
        }

        [Test]
        public void CheckBeforeReleaseBuildOrEdit_pull_master()
        {
            ImageLibrary.minimal_solution_first_ci_build(
                ( universe, world ) =>
                {
                    string cktestCodeCake = "CKTest-CodeCake";
                    NormalizedPath tempDir = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
                    Repository.Clone(
                        "file:///" + Path.GetFullPath( universe.WorldsFolder.AppendPart( world.WorldName.Name ).AppendPart( cktestCodeCake ) ).Replace( "\\", "/" ),
                        tempDir );
                    string testFileName = "testFile";
                    using( Repository concurrentUser = new Repository( tempDir ) )
                    {
                        Branch master = concurrentUser.Branches["master"];
                        Commands.Checkout( concurrentUser, master );
                        NormalizedPath testFile = tempDir.AppendPart( testFileName );
                        File.AppendAllText( testFile, "test" );
                        Commands.Stage( concurrentUser, testFile );
                        Signature testSignature = new Signature( "CKlitest", "nobody@test.com", DateTimeOffset.Now );
                        concurrentUser.Commit( "Test commit.", testSignature, testSignature );
                        concurrentUser.Network.Push( master );
                    }

                    world.CheckBeforeReleaseBuildOrEdit( TestHelper.Monitor, true );
                    foreach( IGitRepository repository in world.GitRepositories )
                    {
                        (bool Success, bool ReloadNeeded) = repository.Pull( TestHelper.Monitor );
                        Success.Should().BeTrue();
                    }
                    NormalizedPath repoPath = universe.DevDirectory.AppendPart( "CKTest-Build" ).AppendPart( cktestCodeCake );
                    using( Repository repo = new Repository( repoPath ) )
                    {
                        Commands.Checkout( repo, repo.Branches["master"] );
                    }
                    File.Exists( Path.GetFullPath( repoPath.AppendPart( testFileName ) ) ).Should().BeTrue();

                }, TestHelper.IsExplicitAllowed );
        }
    }
}
