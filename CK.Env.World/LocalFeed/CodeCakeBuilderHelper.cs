using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public static class CodeCakeBuilderHelper
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

        /// <summary>
        /// Gets the CodeCakeBuilder executable published file path an version (with a null version if
        /// CodeCakeBuilder does not exist).
        /// Currently in netcoreapp2.1.
        /// </summary>
        /// <param name="git">The Git repository.</param>
        /// <returns>The path and version. Version is null if the file does not exist.</returns>
        public static (string CCBExePath, SVersion Version) GetExecutableInfo( IGitRepository git )
        {
            string solutionPath = git.FullPhysicalPath;
            var ccbPath = GetExecutablePath( git.FullPhysicalPath );
            return  (ccbPath, File.Exists( ccbPath ) ? GetVersion( ccbPath ) : null);
        }
    }
}
