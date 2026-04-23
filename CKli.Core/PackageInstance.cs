using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CKli;

/// <summary>
/// Encapsulates a package@version information. Can be used fo other kind of packages than NuGet.
/// <para>
/// <see name="PackageId"/> uses <see cref="StringComparer.Ordinal"/>.
/// Equality and comparison is based on <see name="PackageId"/> first and then <see cref="Version"/>.
/// </para>
/// </summary>
public partial class PackageInstance : IComparable<PackageInstance>, IEquatable<PackageInstance>
{
    readonly string _packageId;
    readonly SVersion _version;
    string? _toString;

    /// <summary>
    /// Initializes a new package instance.
    /// </summary>
    /// <param name="packageId">The package name.</param>
    /// <param name="version">The version of this instance.</param>
    public PackageInstance( string packageId, SVersion version )
    {
        Throw.CheckNotNullArgument( packageId );
        Throw.CheckNotNullArgument( version );
        _packageId = packageId;
        _version = version;
    }

    /// <summary>
    /// Gets the package name.
    /// </summary>
    public string PackageId => _packageId;

    /// <summary>
    /// Gets the version of this instance.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the "package.version.nupkg" file name.
    /// </summary>
    /// <returns>The NuGet package file name.</returns>
    public string ToNupkgFileName() => $"{PackageId}.{Version}.nupkg";

    /// <summary>
    /// Tries to match "xxx@version" pattern (that is the <see cref="ToString()"/> representation).
    /// The <paramref name="head"/> is not forwarded on error.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="instance">The instance on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, [NotNullWhen( true )] out PackageInstance? instance )
    {
        instance = null;
        int idx = head.IndexOf( '@' );
        if( idx <= 0 ) return false;
        var rest = head.Slice( idx + 1 );
        var v = SVersion.TryParse( ref rest );
        if( !v.IsValid ) return false;

        instance = new PackageInstance( new string( head.Slice( 0, idx ) ), v );
        head = rest;
        return true;
    }

    /// <summary>
    /// Tries to parse "xxx@version" pattern (that is the <see cref="ToString()"/> representation).
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="instance">The instance on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( ReadOnlySpan<char> s, [NotNullWhen( true )] out PackageInstance? instance ) => TryMatch( ref s, out instance );

    /// <summary>
    /// Supports ordering by <see cref="PackageId"/> (case insensitive) and <see cref="Version"/>.
    /// </summary>
    /// <param name="other">The other package.</param>
    /// <returns>Standard comparison value.</returns>
    public int CompareTo( PackageInstance? other )
    {
        if( other is null ) return 1;
        int cmp = StringComparer.OrdinalIgnoreCase.Compare( _packageId, other._packageId );
        return cmp == 0 ? _version.CompareTo( other._version ) : cmp;
    }

    /// <summary>
    /// Overridden to consider case insensitive <see cref="PackageId"/>.
    /// </summary>
    /// <param name="other">The other instance.</param>
    /// <returns>True if this is the package identifier (case insensitive) and version as the other one, false otherwise.</returns>
    public bool Equals( PackageInstance? other ) => other is not null
                                                    && StringComparer.OrdinalIgnoreCase.Equals( _packageId, other._packageId )
                                                    && _version == other._version;

    /// <summary>
    /// Overridden to call <see cref="Equals(PackageInstance?)"/>.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns><c>true</c> if the specified object is equal to the current object; otherwise, <c>false</c>.</returns>
    public override bool Equals( object? obj ) => obj is PackageInstance p && Equals( p );

    /// <summary>
    /// Overridden to consider a case insensitive <see cref="PackageId"/>.
    /// </summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() => HashCode.Combine( StringComparer.OrdinalIgnoreCase.GetHashCode( _packageId ), _version.GetHashCode() );

    /// <summary>
    /// Parses the result of a "dotnet package list" call.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="jsonPackageList">The json string to parse.</param>
    /// <param name="buildInfo">Any object: its ToString() method will be used for error and warning logs.</param>
    /// <param name="packages">The consumed package instances.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool ReadConsumedPackages( IActivityMonitor monitor,
                                             string jsonPackageList,
                                             object buildInfo,
                                             out ImmutableArray<PackageInstance> packages )
    {
        try
        {
            using var d = JsonDocument.Parse( jsonPackageList );
            if( !ReadProblems( monitor, buildInfo, d ) )
            {
                packages = [];
                return false;
            }
            packages = ReadPackages( d );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While reading Package list for '{buildInfo}'.", ex );
            packages = [];
            return false;
        }

        static bool ReadProblems( IActivityMonitor monitor, object buildInfo, JsonDocument d )
        {
            bool hasWarning = false;
            if( d.RootElement.TryGetProperty( "problems"u8, out var problems ) )
            {
                foreach( var p in problems.EnumerateArray() )
                {
                    if( p.GetProperty( "level"u8 ).GetString() == "error" )
                    {
                        monitor.Error( $"Package list for '{buildInfo}' has errors. See logs." );
                        return false;
                    }
                    else
                    {
                        hasWarning = true;
                    }
                }
            }
            if( hasWarning )
            {
                monitor.Warn( $"Package list for '{buildInfo}' has warnings. See logs." );
            }
            return true;
        }

        static ImmutableArray<PackageInstance> ReadPackages( JsonDocument d )
        {
            var result = new SortedSet<PackageInstance>();
            foreach( var p in d.RootElement.GetProperty( "projects"u8 ).EnumerateArray() )
            {
                foreach( var f in p.GetProperty( "frameworks"u8 ).EnumerateArray() )
                {
                    foreach( var package in f.GetProperty( "topLevelPackages"u8 ).EnumerateArray() )
                    {
                        string? packageId = package.GetProperty( "id"u8 ).GetString();
                        if( string.IsNullOrWhiteSpace( packageId ) )
                        {
                            Throw.InvalidDataException( $"Null or empty 'topLevelPackages.id' property." );
                        }
                        result.Add( new PackageInstance( packageId,
                                                              SVersion.Parse( package.GetProperty( "resolvedVersion"u8 ).GetString() ) ) );
                    }
                }
            }
            return result.ToImmutableArray();
        }

    }


    /// <summary>
    /// Tries to parse a "package.version.nupkg" file name.
    /// </summary>
    /// <param name="fileName">The file name to parse.</param>
    /// <param name="version">The non null parsed version on success.</param>
    /// <param name="packageIdLength">The package name length.</param>
    /// <returns>True on success, false if the file name cannot be parsed.</returns>
    public static bool TryParseNupkgFileName( ReadOnlySpan<char> fileName,
                                              [NotNullWhen( true )] out SVersion? version,
                                              out int packageIdLength )
    {
        if( fileName.EndsWith( ".nupkg", StringComparison.Ordinal ) )
        {
            var h = fileName[..^6];
            for(; ; )
            {
                var nextDot = h.IndexOf( '.' );
                // Consider 2 consecutive .. to be invalid.
                if( nextDot <= 0 ) break;
                h = h.Slice( nextDot + 1 );
                if( h.Length > 0 && char.IsAsciiDigit( h[0] ) )
                {
                    packageIdLength = fileName.Length - h.Length - 1;
                    version = SVersion.TryParse( ref h );
                    // Here we allow a starting digit in the package id because this is legit (tested):
                    // Successfully created package '...\package\debug\Truc.0Machin.1.0.0.nupkg
                    if( version.IsValid )
                    {
                        return h.Length == 0;
                    }
                }
            }
        }
        version = null;
        packageIdLength = 0;
        return false;
    }

    /// <summary>
    /// Returns "<see cref="PackageId"/>@<see cref="Version"/>".
    /// </summary>
    /// <returns>The package@version string.</returns>
    public override string ToString() => _toString ??= $"{_packageId}@{_version}";
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

    public static bool operator ==( PackageInstance? left, PackageInstance? right )
    {
        return left is null ? right is null : left.Equals( right );
    }

    public static bool operator !=( PackageInstance? left, PackageInstance? right )
    {
        return !(left == right);
    }

    public static bool operator <( PackageInstance? left, PackageInstance? right )
    {
        return left is null ? right is not null : left.CompareTo( right ) < 0;
    }

    public static bool operator <=( PackageInstance? left, PackageInstance? right )
    {
        return left is null || left.CompareTo( right ) <= 0;
    }

    public static bool operator >( PackageInstance? left, PackageInstance? right )
    {
        return left is not null && left.CompareTo( right ) > 0;
    }

    public static bool operator >=( PackageInstance? left, PackageInstance? right )
    {
        return left is null ? right is null : left.CompareTo( right ) >= 0;
    }

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
