using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using static CK.Testing.MonitorTestHelper;
using LibGit2Sharp;
using System.Linq;
using CodeCake;

namespace CK.Env.Tests.LocalTestHelper
{
    public class ProcessHelper
    {
        public IActivityMonitor Monitor { get; set; }
        public NormalizedPath ScriptDirectory { get; set; }
        public string NpmScript { get; set; }
        public string GitScript { get; set; }

        public ProcessHelper()
        {
            Monitor = TestHelper.Monitor;
            ScriptDirectory = TestHelper.TestProjectFolder.AppendPart( "Scripts" );
            NpmScript = "CreateNpmProject.ps1";
            GitScript = "CreateGitRepository.ps1";
        }

        public void CreateGitRepository( string localPath, string remotePath )
        {
            var remoteRepoPath = Repository.Init( remotePath, true );

            var localRepoPath = Repository.Init( localPath );

            using( var localRepo = new Repository( localRepoPath ) )
            {
                Commands.Stage( localRepo, "*" );
                localRepo.Commit
                (
                    "First Commit",
                    new Signature( new Identity( "Aymeric", "aymeric.richard@signature-code.com" ), new DateTimeOffset( DateTime.Now ) ),
                    new Signature( new Identity( "Aymeric", "aymeric.richard@signature-code.com" ), new DateTimeOffset( DateTime.Now ) )
                );
                var remote = localRepo.Network.Remotes.Add( "origin", remoteRepoPath );
                var localBranch = localRepo.Branches["master"];

                localRepo.Branches.Update
                (
                    localBranch,
                    b => b.Remote = remote.Name,
                    b => b.UpstreamBranch = localBranch.CanonicalName
                );


                localRepo.Network.Push( localBranch );
            }
        }

        public string CreateNpmProject( string path, string projectName )
        {
            var gitignore = File.ReadAllText( TestHelper.TestProjectFolder.AppendPart( "Gitignore" ).AppendPart( "node" ) );

            //StandardGlobalInfo
            // NPMPublishedProject.Create(
            //                null,
            //                solution,
            //                (string)item.Attribute( "Path" ),
            //                (string)item.Attribute( "OutputFolder" ) );


            //SimplePackageJsonFile.Create()
            throw new NotImplementedException();
        }

        /// <summary>
        /// Create a minimal npm project
        /// </summary>
        /// <param name="path">The project will be created in a directory inside this path.</param>
        /// <param name="projectName">Name of the project, which will be created a directory with the same name.</param>
        public string CreateNpmProject( string path, string projectName, bool todelete )
        {
            var fullPath = new NormalizedPath( path ).AppendPart( projectName );
            Directory.CreateDirectory( fullPath );

            RunProcess
                (
                    fullPath,
                    ScriptDirectory.AppendPart( NpmScript ),
                    new List<string>(),
                    Monitor
                );

            return fullPath;
        }

        /// <summary>
        /// Create local git repository and push first commit to remote.
        /// </summary>
        /// <param name="localPath">Location where an existing project is.</param>
        /// <param name="remotePath">Local file path where to store the remote repository.</param>
        public void CreateGitRepository( string localPath, string remotePath, bool todelete )
        {

            var arguments = new List<string>()
            {
                remotePath,
                localPath
            };

            RunProcess
            (
                localPath,
                ScriptDirectory.AppendPart( GitScript ),
                arguments,
                Monitor
            );
        }

        void RunProcess
        (
            string workingDir,
            string commandFileName,
            IEnumerable<string> commandArguments,
            IActivityMonitor activityMonitor,
            IEnumerable<(string, string)> envVariables = null
        )
        {
            if
            (
                ProcessRunner.RunPowerShell
                (
                    activityMonitor,
                    workingDir,
                    commandFileName,
                    commandArguments,
                    CK.Core.LogLevel.Warn,
                    envVariables
                )
            )
                throw new Exception( $"Failed to run {commandFileName}" );
        }
    }
}
