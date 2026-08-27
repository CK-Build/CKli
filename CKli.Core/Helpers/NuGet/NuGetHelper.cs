using CK.Core;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// NuGet related helpers.
/// </summary>
public static partial class NuGetHelper
{
    /// <summary>
    /// Helper that removes a NuGet source or (re)configures it.
    /// When set, the source is moved to the first position in both &lt;packageSources&gt; and &lt;packageSourceMapping&gt;.
    /// See <see href="https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#enable-by-manually-editing-nugetconfig"/>. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="root">The &lt;configuration&gt; root of the <c>nuget.config</c> file.</param>
    /// <param name="name">The name of the source.</param>
    /// <param name="sourceUrl">The source url. Null to remove it.</param>
    /// <param name="patterns">Optional patterns. When empty, "*" is used.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool SetOrRemoveNuGetSource( IActivityMonitor monitor,
                                               XElement root,
                                               string name,
                                               string? sourceUrl,
                                               params string[] patterns )
    {
        var packageSources = root.Elements( XNames.PackageSources ).FirstOrDefault();
        if( packageSources == null )
        {
            monitor.Error( $"Unable to find <packageSources> element in:{Environment.NewLine}{root}" );
            return false;
        }
        // Fix missing mapping first.
        var mappings = root.Elements( XNames.PackageSourceMapping ).FirstOrDefault();
        if( mappings == null )
        {
            mappings = new XElement( XNames.PackageSourceMapping,
                                     packageSources.Elements( XNames.Add ).Attributes( XNames.Key )
                                        .Select( k => new XElement( XNames.PackageSource, new XAttribute( XNames.Key, k.Value ),
                                                        new XElement( XNames.Package, new XAttribute( XNames.Pattern, "*" ) ) ) ) );
            root.Add( mappings );
            monitor.Trace( $"Missing <packageSourceMapping>, it is now fixed:{Environment.NewLine}{root}" );
        }
        // First, removes.
        var existing = packageSources.Elements( XNames.Add ).FirstOrDefault( e => StringComparer.OrdinalIgnoreCase.Equals( name, (string?)e.Attribute( XNames.Key ) ) );
        if( existing != null )
        {
            existing.Remove();
            existing = mappings.Elements( XNames.PackageSource ).FirstOrDefault( e => StringComparer.OrdinalIgnoreCase.Equals( name, (string?)e.Attribute( XNames.Key ) ) );
            existing?.Remove();
        }
        if( sourceUrl != null )
        {
            // Adds the source itself... but not before the </clear> elements!
            var newOne = new XElement( XNames.Add, new XAttribute( XNames.Key, name ), new XAttribute( XNames.Value, sourceUrl ) );
            var clear = packageSources.Elements( XNames.Clear ).LastOrDefault();
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
            mappings.AddFirst( new XElement( XNames.PackageSource, new XAttribute( XNames.Key, name ),
                                    patterns.Select( p => new XElement( XNames.Package, new XAttribute( XNames.Pattern, p ) ) ) ) );
        }
        return true;
    }

    /// <summary>
    /// Gets the &lt;configuration&gt; root element, checking its name.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="nugetConfigFile">The <c>nuget.config</c> document.</param>
    /// <returns>The configuration root or null on error.</returns>
    public static XElement? GetConfigurationRoot( IActivityMonitor monitor, XDocument nugetConfigFile )
    {
        if( nugetConfigFile.Root?.Name.LocalName != "configuration" )
        {
            monitor.Error( $"Missing <configuration> root element in:{Environment.NewLine}{nugetConfigFile}" );
            return null;
        }
        return nugetConfigFile.Root;
    }

    /// <summary>
    /// Reads the &lt;configuration&gt; root element from a file, checking its name.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="nugetConfigFilePath">The <c>nuget.config</c> file path.</param>
    /// <returns>The configuration root or null on error.</returns>
    public static XElement? GetConfigurationRoot( IActivityMonitor monitor, string nugetConfigFilePath )
    {
        try
        {
            return GetConfigurationRoot( monitor, XDocument.Load( nugetConfigFilePath ) );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading '{nugetConfigFilePath}'.", ex );
            return null;
        }
    }

    /// <summary>
    /// Reads the &lt;configuration&gt; root element from a stream, checking its name.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="s">A read stream on a <c>nuget.config</c> content.</param>
    /// <returns>The configuration root or null on error.</returns>
    public static XElement? GetConfigurationRoot( IActivityMonitor monitor, Stream s )
    {
        try
        {
            return GetConfigurationRoot( monitor, XDocument.Load( s ) );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading stream.", ex );
            return null;
        }
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
            var canarySource = Path.Combine( Cache.GetGlobalCachePath( monitor ), "ck.canarypackage/1.0.0" );
            if( !Directory.Exists( canarySource ) )
            {
                monitor.Error( $"""
                    Cannot find 'ck.canarypackage/1.0.0' installed NuGet package in '{Cache.GetGlobalCachePath( monitor )}'.
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
