using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    static class CodeCakeBuilderHelper
    {
        public static readonly string CodeCakeBuilderRelativePath = "CodeCakeBuilder/bin/Debug/netcoreapp2.1/publish/CodeCakeBuilder.dll";

        /// <summary>
        /// Gets the CodeCakeBuilder executable file path.
        /// Currently in netcoreapp2.1.
        /// </summary>
        /// <param name="solutionFolderPhysicalPath">The solution folder path.</param>
        /// <returns>Path to the CodeCakeBuilder.dll for this solution.</returns>
        public static string GetExecutablePath( string solutionFolderPhysicalPath )
        {
            return System.IO.Path.Combine( solutionFolderPhysicalPath, CodeCakeBuilderRelativePath );
        }

        /// <summary>
        /// Gets the version of the solution's CodeCakeBuilder executable file that must exist.
        /// </summary>
        /// <param name="ccbPath">The dll or exe file path.</param>
        /// <returns>The version from the <see cref="InformationalVersion.Parse"/>.</returns>
        public static SVersion GetVersion( string ccbPath )
        {
            return InformationalVersion.Parse( FileVersionInfo.GetVersionInfo( ccbPath ).ProductVersion ).NuGetVersion;
        }

    }
}
