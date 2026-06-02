using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CKli.Core;

public static partial class NuGetHelper
{
    /// <summary>
    /// Helper methods about the NuGet <see cref="GlobalCachePath"/>.
    /// </summary>
    public static class Cache
    {
        static string? _globalCachePath;

        /// <summary>
        /// Gets the NuGet global cache path.
        /// </summary>
        public static string GlobalCachePath => _globalCachePath ??= Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".nuget/packages" );

        /// <summary>
        /// Removes a package instance or all versions of a package from NuGet global cache.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <param name="version">Version to remove or null to remove all versions.</param>
        /// <returns>True on success, false if an error occurred while deleting the cached folder.</returns>
        public static bool RemovePackage( IActivityMonitor monitor, string packageId, SVersion? version )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
            Throw.CheckArgument( version == null || version.IsValid );
            var p = Path.Combine( GlobalCachePath, packageId.ToLowerInvariant() );
            if( version != null ) p = Path.Combine( p, version.ToString() );
            if( Directory.Exists( p ) )
            {
                monitor.Trace( version != null
                                ? $"Removing package '{packageId}@{version}' from NuGet global cache."
                                : $"Removing all versions of package '{packageId}' from NuGet global cache." );

                return FileHelper.DeleteFolder( monitor, p );
            }
            return true;
        }

        /// <summary>
        /// Gets the versions available for a package identifier.
        /// <para>
        /// Caution: There is no specific order.
        /// </para>
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <returns>The available versions.</returns>
        public static IEnumerable<SVersion> GetAvailableVersions( string packageId )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
            var p = Path.Combine( GlobalCachePath, packageId.ToLowerInvariant() );
            if( Directory.Exists( p ) )
            {
                return Directory.EnumerateDirectories( p )
                                .Select( v => SVersion.TryParse( Path.GetFileName( v ) ) )
                                .Where( v => v.IsValid );
            }
            return [];
        }

        /// <summary>
        /// Gets whether the given package instance is locally available in this cache.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package version.</param>
        /// <returns>Whether the package instance is in the global cache.</returns>
        public static bool IsAvailable( string packageId, SVersion version )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
            Throw.CheckArgument( version.IsValid );
            string p = GetPackagePath( packageId, version );
            return Directory.Exists( p );
        }

        static string GetPackagePath( string packageId, SVersion version ) => Path.Combine( GlobalCachePath, packageId.ToLowerInvariant(), version.ToString() );
    }

}
