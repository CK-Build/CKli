using CK.Core;
using System;

namespace CKli;

/// <summary>
/// Extends <see cref="SVersion"/>.
/// </summary>
public static class SVersionExtensions
{
    /// <summary>
    /// Gets whether <see cref="SVersion.ParsedPrefix"/> is "local/".
    /// </summary>
    /// <param name="version">This version.</param>
    /// <returns>True if this is a "local/" prefixed version.</returns>
    public static bool IsLocal( this SVersion version ) => version.ParsedPrefix.AsSpan().Equals( "local/", StringComparison.Ordinal );

    public static bool IsStableRoughBaseOf( this SVersion version, SVersion target )
    {
        Throw.CheckState( version.IsStable );
        if( version.Major == target.Major )
        {
            return version.Minor == target.Minor
                    ? version.Patch == target.Patch || version.Patch == target.Patch + 1
                    : version.Minor == target.Minor + 1 && target.Patch == 0;
        }
        return version.Major == target.Major + 1 && target.Minor == 0 && target.Patch == 0;
    }

}
