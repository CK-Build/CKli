using Cake.Common.Diagnostics;
using CK.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeCake
{
    public sealed class AngularWorkspace : NPMProjectContainer
    {
        public NPMProject WorkspaceProject { get; }

        AngularWorkspace( NPMProject workspaceProject, IReadOnlyList<NPMProject> projects )
        {
            WorkspaceProject = workspaceProject;
            foreach( var p in projects )
            {
                Add( p );
            }
        }

        public static AngularWorkspace Create( NPMSolution npmSolution, NormalizedPath path, bool useYarn )
        {
            NormalizedPath packageJsonPath = path.AppendPart( "package.json" );
            NormalizedPath angularJsonPath = path.AppendPart( "angular.json" );

            JObject packageJson = JObject.Parse( File.ReadAllText( packageJsonPath ) );
            JObject angularJson = JObject.Parse( File.ReadAllText( angularJsonPath ) );
            if( !(packageJson["private"]?.ToObject<bool>() ?? false) ) throw new InvalidDataException( "A workspace project should be private." );
            List<NPMProject> projects = new List<NPMProject>();
            var jsonProject = angularJson["projects"].ToObject<JObject>();
            foreach( var project in jsonProject.Properties() )
            {
                var projectPath = project.Value["root"].ToString();

                // The "outputPath" is for Applications.
                var options = project.Value["architect"]["build"]["options"];
                string outputPathJson = options["outputPath"]?.Value<string>();
                bool havePath = outputPathJson != null;
                // The "project" is for libraries.
                string ngPackagePath = options["project"]?.Value<string>();
                bool haveNgPackageJson = ngPackagePath != null;
                if( havePath && haveNgPackageJson )
                {
                    Throw.NotSupportedException( $"File '{angularJsonPath}' has both architect/build/options/outputPath (application) and project (library) properties." );
                }
                NormalizedPath outputPath;
                NormalizedPath ngPackagePathFullPath = path.Combine( ngPackagePath );
                if( haveNgPackageJson )
                {
                    JObject ngPackage = JObject.Parse( File.ReadAllText( ngPackagePathFullPath ) );
                    string dest = ngPackage["dest"]?.Value<string>();
                    if( dest == null ) throw new InvalidDataException( "ng package does not contain dest path." );
                    outputPath = ngPackagePathFullPath.RemoveLastPart().Combine( dest ).ResolveDots();
                }
                else if( havePath )
                {
                    outputPath = path.Combine( outputPathJson );
                }
                else
                {
                    npmSolution.GlobalInfo.Cake.Warning( $"No architect/build/options/outputPath (application) nor project (library) found for angular project '{path}'. Using the project's path." );
                    outputPath = path.Combine( projectPath );
                }

                projects.Add( NPMPublishedProject.Create( npmSolution, path.Combine( projectPath ), outputPath, useYarn ) );
            }
            return new AngularWorkspace( NPMPublishedProject.Create( npmSolution, path, path, useYarn ), projects );
        }
    }
}
