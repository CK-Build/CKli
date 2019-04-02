using Cake.Npm;
using System.Collections.Generic;
using System.Linq;

namespace CodeCake
{
    public partial class Build
    {
        void StandardNpmBuild( IEnumerable<string> projects )
        {
            foreach( PackageJson package in projects.Select( PackageJson.FromDirectoryPath ) )
            {
                if( package.Scripts.Contains( "build" ) )
                {
                    Cake.NpmRunScript(
                        "build",
                        s => s
                            .WithLogLevel( NpmLogLevel.Info )
                            .FromPath( package.DirectoryPath )
                    );
                } else
                {
                    Cake.TerminateWithError("No build script found in the package.json.");
                }
            }
        }
    }
}
