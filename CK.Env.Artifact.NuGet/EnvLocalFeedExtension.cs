using CK.Core;
using CK.Env.NuGet;
using CSemVer;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public static class EnvLocalFeedExtension
    {
        /// <summary>
        /// Gets the best version for a NuGet package.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <returns>The version or null if not found.</returns>
        public static SVersion GetBestNuGetVersion( this IEnvLocalFeed @this, IActivityMonitor m, string packageId )
        {
            return EnvLocalFeedProviderExtension.GetMaxVersionFromFeed( @this.PhysicalPath, packageId );
        }

        /// <summary>
        /// Gets a package or null if not found.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <param name="v">The package version.</param>
        /// <returns>The local package file or null if not found.</returns>
        public static LocalNuGetPackageFile GetNuGetPackageFile( this IEnvLocalFeed @this, IActivityMonitor m, string packageId, SVersion v )
        {
            var f = GetPackagePath( @this.PhysicalPath, packageId, v );
            return System.IO.File.Exists( f ) ? new LocalNuGetPackageFile( f, packageId, v ) : null;
        }

        /// <summary>
        /// Gets all package files.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The package files.</returns>
        public static IEnumerable<LocalNuGetPackageFile> GetAllNuGetPackageFiles( this IEnvLocalFeed @this, IActivityMonitor m )
        {
            return System.IO.Directory
                            .EnumerateFiles( @this.PhysicalPath, "*.nupkg" )
                            .Where( f => !f.EndsWith( ".symbols.nupkg" ) )
                            .Select( f => LocalNuGetPackageFile.Parse( f ) );
        }


        static internal string GetPackagePath( string path, string packageId, SVersion v )
        {
            return System.IO.Path.Combine( path, packageId + '.' + v.ToNuGetPackageString() + ".nupkg" );
        }

    }
}
