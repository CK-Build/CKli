using CK.Core;

using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public static class EnvLocalFeedProviderExtension
    {
        static readonly NormalizedPath _localNuGetCache = System.IO.Path.GetFullPath( Environment.ExpandEnvironmentVariables( "%UserProfile%/.nuget/packages/" ) );

        /// <summary>
        /// Removes a specific version of a package from the local
        /// NuGet cache.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package's version.</param>
        public static void RemoveFromNuGetCache( this IEnvLocalFeedProvider @this, IActivityMonitor m, string packageId, SVersion version )
        {
            DoRemoveFromNuGetCache( m, packageId, version );
        }

        internal static void DoRemoveFromNuGetCache( IActivityMonitor m, string packageId, SVersion version )
        {
            var dirPath = _localNuGetCache.AppendPart( packageId ).AppendPart( version.ToNormalizedString() );
            if( FileHelper.RawDeleteLocalDirectory( m, dirPath ) )
            {
                m.Info( $"Removed {packageId} package in version {version} from local NuGet cache." );
            }
        }

        /// <summary>
        /// Checks whether a package exists in the NuGet cache folder.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package's version.</param>
        /// <returns>True if found, false otherwise.</returns>
        public static bool ExistsInNuGetCache( this IEnvLocalFeedProvider @this, IActivityMonitor m, string packageId, SVersion version )
        {
            var dirPath = _localNuGetCache.AppendPart( packageId ).AppendPart( version.ToNormalizedString() );
            return System.IO.Directory.Exists( dirPath );
        }

        static IEnumerable<SVersion> GetAllVersionsFromFeed( string path, string packageId )
        {
            // Do not use TryParse here: pattern MUST be a version since we remove
            // .symbols and "sub packages" (like CK.Text.Virtual for CK.Text by filtering only
            // suffixes that start with a digit.
            // If an error occurs here it should be an exception since this should never happen.
            // Note: Max on reference type returns null on empty source.
            return System.IO.Directory.EnumerateFiles( path, packageId + ".*.nupkg" )
                                .Select( p => System.IO.Path.GetFileName( p ) )
                                .Select( n => n.Substring( packageId.Length + 1, n.Length - packageId.Length - 7 ) )
                                .Where( n => !n.EndsWith( ".symbols" ) && Char.IsDigit( n, 0 ) )
                                .Select( v => SVersion.Parse( v ) );
        }

        internal static SVersion GetMaxVersionFromFeed( string path, string packageId )
        {
            // Note: Max on reference type returns null on empty source.
            return GetAllVersionsFromFeed( path, packageId ).Max( v => v );
        }

        static SVersion GetBestVersionFromNuGetCache( IActivityMonitor m, string packageId )
        {
            // Max on reference type returns null on empty source.
            return System.IO.Directory.GetDirectories( _localNuGetCache.AppendPart( packageId ) )
                .Select( p => SafeParse( m, p ) )
                .Where( v => v != null )
                .Max( v => v );
        }

        static SVersion SafeParse( IActivityMonitor m, string path )
        {
            SVersion v = null;
            int idx = path.LastIndexOf( System.IO.Path.DirectorySeparatorChar );
            if( idx < 0 )
            {
                m.Error( $"Invalid path '{path}' for package." );
            }
            else if( !(v = SVersion.TryParse( path.Substring( idx ) )).IsValid )
            {
                m.Error( $"Invalid SemVer in '{path}' for package." );
            }
            return v;
        }


    }
}
