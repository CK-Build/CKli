using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CKli.Core;

public static partial class NuGetHelper
{
    /// <summary>
    /// Helper methods about the NuGet <see cref="GetGlobalCachePath(IActivityMonitor)"/>.
    /// </summary>
    public static class Cache
    {
        static string? _globalCachePath;

        /// <summary>
        /// Gets the NuGet global cache path.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The NuGet global cache path.</returns>
        public static string GetGlobalCachePath( IActivityMonitor monitor )
        {
            if( _globalCachePath == null )
            {
                var expected = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".nuget/packages" );
                if( !Directory.Exists( expected ) )
                {
                    using( monitor.OpenInfo( $"""
                        Regular path to NuGet global cache doesn't exist: '{expected}'.
                        Calling 'dotnet nuget locals global-packages --list' to resolve the NuGet global cache path.
                        """ ) )
                    {
                        var stdOut = new StringBuilder();
                        int? exitCode = ProcessRunner.RunProcess( monitor,
                                                                  "dotnet",
                                                                  "nuget locals global-packages --list",
                                                                  Environment.CurrentDirectory,
                                                                  stdOut: stdOut );
                        if( exitCode is not 0 )
                        {
                            monitor.Error( $"""
                                Calling 'dotnet nuget locals global-packages --list' exited with error '{exitCode}' exit code.
                                Keeping default path instead of throwing.
                                """ );
                        }
                        else
                        {
                            var output = stdOut.ToString();
                            string? p = null;
                            if( output.StartsWith( "global-packages: " ) )
                            {
                                Throw.DebugAssert( "global-packages: ".Length == 17 );
                                p = output.Substring( 17 ).Trim();
                                if( !Directory.Exists( p ) )
                                {
                                    p = null;
                                }
                            }

                            if( p == null )
                            {
                                monitor.Error( """
                                    Expecting 'dotnet nuget locals global-packages --list' to output 'global-packages: <path>' where <path> is an existing path.
                                    Keeping default path instead of throwing.
                                    """ );
                            }
                            else
                            {
                                monitor.Trace( $"NuGet global cache path is: '{p}'." );
                                expected = p;
                            }
                        }
                    }
                }
                _globalCachePath = expected;
            }
            return _globalCachePath;
        }

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
            var p = Path.Combine( GetGlobalCachePath( monitor ), packageId.ToLowerInvariant() );
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
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <returns>The available versions.</returns>
        public static IEnumerable<SVersion> GetAvailableVersions( IActivityMonitor monitor, string packageId )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
            var p = Path.Combine( GetGlobalCachePath( monitor ), packageId.ToLowerInvariant() );
            if( Directory.Exists( p ) )
            {
                return Directory.EnumerateDirectories( p )
                                .Select( v => SVersion.ParseNoThrow( Path.GetFileName( v ) ) )
                                .Where( v => v.IsValid );
            }
            return [];
        }

        /// <summary>
        /// Gets whether the given package instance is locally available in this cache.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package version.</param>
        /// <returns>Whether the package instance is in the global cache.</returns>
        public static bool IsAvailable( IActivityMonitor monitor, string packageId, SVersion version )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
            Throw.CheckArgument( version.IsValid );
            string p = GetPackagePath( monitor, packageId, version );
            return Directory.Exists( p );
        }

        static string GetPackagePath( IActivityMonitor monitor, string packageId, SVersion version ) => Path.Combine( GetGlobalCachePath( monitor ), packageId.ToLowerInvariant(), version.ToString() );
    }

}
