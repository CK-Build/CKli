using Cake.Npm;
using CSemVer;
using System.Collections.Generic;
using System.IO;
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
                }
                else
                {
                    Cake.TerminateWithError( "No build script found in the package.json." );
                }
            }
        }

        void NpmBuildWithNewVersion( string jsPath, SVersion version )
        {
            using( var versionReplacer = new PackageVersionReplacer( version, Path.Combine( jsPath, "package.json" ) ) )
            {
                // npm run build
                Cake.NpmRunScript(
                    "build",
                    s => s
                        .WithLogLevel( NpmLogLevel.Info )
                        .FromPath( jsPath )
                );
            }//The old version is restored
        }
    }
}
