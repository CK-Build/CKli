using Cake.Npm;
using System.Collections.Generic;
using System.Linq;

namespace CodeCake
{
    public partial class Build
    {
        void StandardNpmUnitTests( IEnumerable<string> projects )
        {
            foreach( PackageJson package in projects.Select( PackageJson.FromDirectoryPath ))
            {
                if( package.Scripts.Contains( "test" ) )
                {
                    Cake.NpmRunScript(
                        "test",
                        s => s
                            .WithLogLevel( NpmLogLevel.Info )
                            .FromPath( package.DirectoryPath )
                    );
                }
                else
                {
                    Cake.TerminateWithError( "No test script found in the package.json of the package " + package.ArtifactInstance.Artifact.Name );
                }
            }
        }
    }
}
