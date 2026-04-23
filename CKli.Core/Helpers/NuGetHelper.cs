using CK.Core;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// NuGet related helpers.
/// </summary>
public static class NuGetHelper
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

    /// <summary>
    /// Helper that removes a NuGet source or (re)configures it.
    /// When set, the source is moved to the first position in both &lt;packageSources&gt; and &lt;packageSourceMapping&gt;.
    /// See <see href="https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#enable-by-manually-editing-nugetconfig"/>. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="nugetConfigFile">The configuration xml file.</param>
    /// <param name="name">The name of the source.</param>
    /// <param name="sourceUrl">The source url. Null to remove it.</param>
    /// <param name="patterns">Optional patterns. When empty, "*" is used.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool SetOrRemoveNuGetSource( IActivityMonitor monitor,
                                               XDocument nugetConfigFile,
                                               string name,
                                               string? sourceUrl,
                                               params string[] patterns )
    {
        if( nugetConfigFile.Root?.Name.LocalName != "configuration" )
        {
            monitor.Error( $"Missing <configuration> root element in:{Environment.NewLine}{nugetConfigFile}" );
            return false;
        }
        var root = nugetConfigFile.Root;
        var packageSources = root.Elements( "packageSources" ).FirstOrDefault();
        if( packageSources == null )
        {
            monitor.Error( $"Unable to find <packageSources> element in:{Environment.NewLine}{nugetConfigFile}" );
            return false;
        }
        // Fix missing mapping first.
        var mappings = root.Elements( "packageSourceMapping" ).FirstOrDefault();
        if( mappings == null )
        {
            mappings = new XElement( "packageSourceMapping",
                                     packageSources.Elements( "add" ).Attributes( "key" )
                                        .Select( k => new XElement( "packageSource", new XAttribute( "key", k.Value ),
                                                        new XElement( "package", new XAttribute( "pattern", "*" ) ) ) ) );
            root.Add( mappings );
            monitor.Trace( $"Missing <packageSourceMapping>, it is now fixed:{Environment.NewLine}{nugetConfigFile}" );
        }
        // First, removes.
        var existing = packageSources.Elements( "add" ).FirstOrDefault( e => StringComparer.OrdinalIgnoreCase.Equals( name, (string?)e.Attribute( "key" ) ) );
        if( existing != null )
        {
            existing.Remove();
            existing = mappings.Elements( "packageSource" ).FirstOrDefault( e => StringComparer.OrdinalIgnoreCase.Equals( name, (string?)e.Attribute( "key" ) ) );
            existing?.Remove();
        }
        if( sourceUrl != null )
        {
            // Adds the source itself... but not before the </clear> elements!
            var newOne = new XElement( "add", new XAttribute( "key", name ), new XAttribute( "value", sourceUrl ) );
            var clear = packageSources.Elements( "clear" ).LastOrDefault();
            if( clear != null )
            {
                clear.AddAfterSelf( newOne );
            }
            else
            {
                packageSources.AddFirst( newOne );
            }
            // And its mappings in first position.
            if( patterns.Length == 0 ) patterns = ["*"];
            mappings.AddFirst( new XElement( "packageSource", new XAttribute( "key", name ),
                                    patterns.Select( p => new XElement( "package", new XAttribute( "pattern", p ) ) ) ) );
        }
        return true;
    }

    /// <summary>
    /// Creates a local NuGey V3 feed (name/version folder layout with expanded packages).
    /// The <paramref name="localFolderPath"/> must be fully qualified and is created if it
    /// doesn't exist.
    /// <para>
    /// Adding packages to this kind of feed MUST use NuGet, not simple package copy: <see cref="PushToLocalFeed"/>
    /// must be used.
    /// </para>
    /// <para>
    /// This uses the CK.CanaryPackage that is available in the global NuGet cache to
    /// initialize the local feed (because this CKli.Core assembly references it).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="localFolderPath">The feed path.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool EnsureLocalFeed( IActivityMonitor monitor, string localFolderPath )
    {
        Throw.CheckArgument( Path.IsPathFullyQualified( localFolderPath ) );
        var canaryPath = Path.Combine( localFolderPath, "ck.canarypackage/1.0.0" );
        if( !Directory.Exists( canaryPath ) )
        {
            var canarySource = Path.Combine( Cache.GlobalCachePath, "ck.canarypackage/1.0.0" );
            if( !Directory.Exists( canarySource ) )
            {
                monitor.Error( $"""
                    Cannot find 'ck.canarypackage/1.0.0' installed NuGet package in '{Cache.GlobalCachePath}'.
                    This package is installed with CKli.Core and has no reason to be missing.
                    """ );
                return false;
            }
            try
            {
                FileUtil.CopyDirectory( new DirectoryInfo( canarySource ), new DirectoryInfo( canaryPath ) );
            }
            catch( Exception ex )
            {
                monitor.Error( "While creating NuGet local feed.", ex );
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// "dotnet nuget push" a .nupkg file to a <paramref name="localFolderPath"/>.
    /// <para>
    /// The target local folder should have been prepared by <see cref="EnsureLocalFeed(IActivityMonitor, string)"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="nupkgFilePath">The .nupkg file path.</param>
    /// <param name="localFolderPath">The target NuGet local feed.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool PushToLocalFeed( IActivityMonitor monitor, string nupkgFilePath, string localFolderPath )
    {
        ReadOnlySpan<char> fileName = Path.GetFileName( nupkgFilePath.AsSpan() );
        if( !PackageInstance.TryParseNupkgFileName( fileName, out var _, out var _ ) )
        {
            monitor.Error( $"Invalid NuGet package file name: {nupkgFilePath}." );
            return false; 
        }
        if( !File.Exists( nupkgFilePath ) )
        {
            monitor.Error( $"Missing NuGet package file to push: '{nupkgFilePath}'." );
            return false;
        }
        if( !Directory.Exists( localFolderPath ) )
        {
            monitor.Error( $"Target local NuGet feed folder is missing: '{localFolderPath}'." );
            return false;
        }
        using var gLog = monitor.OpenTrace( $"Pushing package '{fileName}' into local NuGet feed '{localFolderPath}'." );
        int? exitCode = ProcessRunner.RunProcess( monitor,
                                                  "dotnet",
                                                  $"""
                                                  nuget push "{nupkgFilePath}" -s "{localFolderPath}"
                                                  """,
                                                  localFolderPath );
        if( exitCode != 0 )
        {
            monitor.CloseGroup( $"Failed to push '{fileName}' package . Exit code = '{exitCode}'." );
            return false;
        }
        return true;
    }

}
