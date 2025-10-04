using CK.Core;
using CSemVer;
using System;
using System.Diagnostics.CodeAnalysis;

namespace CKli;

/// <summary>
/// Minimal package instance. There's no constraint on the <see cref="PackageId"/> except that it cannot
/// be empty or whitespace. This applies to any package management system.
/// </summary>
public sealed class PackageInstance
{
    readonly string _packageId;
    readonly SVersion _version;

    /// <summary>
    /// Get the package name.
    /// </summary>
    public string PackageId => _packageId;

    /// <summary>
    /// Get the version.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Initializes a new valid package instance.
    /// </summary>
    /// <param name="name">Package name.</param>
    /// <param name="version">Package version.</param>
    public PackageInstance( string name, SVersion version )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckNotNullArgument( version );
        _packageId = name;
        _version = version;
    }

    PackageInstance( SVersion version, string name )
    {
        _packageId = name;
        _version = version;
    }

    /// <summary>
    /// Tries to parse a "packageId@version" name.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="result">The non null result on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( ReadOnlySpan<char> s, [NotNullWhen( true )] out PackageInstance? result )
    {
        result = null;
        s = s.Trim();
        int idxAt = s.IndexOf( '@' );
        if( idxAt <= 0 ) return false;
        var n = s.Slice( 0, idxAt );
        var v = SVersion.TryParse( ref s );
        if( !v.IsValid ) return false;
        result = new PackageInstance( v, new string( n ) );
        return true;
    }
}
