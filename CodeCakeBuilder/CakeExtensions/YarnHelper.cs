using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CodeCakeBuilder.CakeExtensions
{
    public class YarnHelper
    {
        static NormalizedPath? TryFindYarn( NormalizedPath currentDirectory )
        {
            while( currentDirectory.HasParts )
            {
                NormalizedPath releases = Path.GetFullPath( currentDirectory.Combine( ".yarn/releases" ));
                if( Directory.Exists( releases ) )
                {
                    var yarn = Directory.GetFiles( releases )
                        .Select(s=> Path.GetFileName(s))
                        .Where( s => s.StartsWith( "yarn" ) )
                        .Where( s => s.EndsWith( "js" ) ) // There is no dot on purpose, a js file can be js/mjs/cjs/whatever they invent next
                        .FirstOrDefault();
                    if( yarn != null ) return releases.AppendPart( yarn );
                }
                currentDirectory = currentDirectory.RemoveLastPart();
            }
            return default;
        }

        public static string Info( string packageName, string workingDirectory )
        {
            var yarn = TryFindYarn( workingDirectory );
            if( !yarn.HasValue )
            {
                throw new InvalidDataException( "Could not find yarn binaries." );
            }
            var info = new ProcessStartInfo( "node", $"{yarn} info {packageName} --json" )
            {
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                WorkingDirectory = workingDirectory
            };
            var process = new Process
            {
                StartInfo = info,
            };
            process.Start();
            process.WaitForExit();
            return process.StandardOutput.ReadToEnd();
        }
    }
}
