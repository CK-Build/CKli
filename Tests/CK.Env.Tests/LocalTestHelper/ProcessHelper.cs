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
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace CK.Env.Tests.LocalTestHelper
{
    public class ProcessHelper
    {
        public IActivityMonitor Monitor { get; set; }
        //public NormalizedPath ScriptDirectory { get; set; }
        //public string NpmScript { get; set; }
        //public string GitScript { get; set; }

        public ProcessHelper()
        {
            Monitor = TestHelper.Monitor;
            //ScriptDirectory = TestHelper.TestProjectFolder.AppendPart( "Scripts" );
            //NpmScript = "CreateNpmProject.ps1";
            //GitScript = "CreateGitRepository.ps1";
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

        /// <summary>
        /// Create a package.json and a .gitignore.
        /// Package.json is not yet generated.
        /// Different packages types will be supported in the future, for example to provide different .gitignore.
        /// </summary>
        /// <param name="path">Path to output the project.</param>
        /// <param name="projectName">Project name, files will be output inside a folder name as it.</param>
        /// <returns></returns>
        public NormalizedPath CreateNpmProject( NormalizedPath path, string projectName )
        {
            var gitignorePath = TestHelper.TestProjectFolder.AppendPart( "Gitignore" ).AppendPart( "node" ).AppendPart( ".gitignore" );
            var packageJsonPath = TestHelper.TestProjectFolder.AppendPart( "NpmFiles" ).AppendPart( "package.json" );

            var jObject = JsonConvert.DeserializeObject<JObject>( File.ReadAllText( packageJsonPath ) );
            jObject.SelectToken( "name" ).Replace( projectName );
            var tempPath = new NormalizedPath( Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() ) ).AppendPart( "package.json" );
            Directory.CreateDirectory( tempPath.RemoveLastPart() );
            File.WriteAllText( tempPath, jObject.ToString() );

            List<NormalizedPath> allFilesPaths = new List<NormalizedPath>()
            {
                TestHelper.TestProjectFolder.AppendPart( "NpmFiles" ).AppendPart( "index.js" ),
                gitignorePath,
                tempPath
            };

            foreach( var file in allFilesPaths )
            {
                File.Copy( file, path.AppendPart(file.LastPart), true );
            }
            return path.AppendPart( projectName );
        }
    }
}
