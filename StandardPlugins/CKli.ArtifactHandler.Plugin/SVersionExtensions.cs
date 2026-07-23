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


    public static SVersion ToNextVersion( this SVersion thisVersion, SVersionChange vChange, string? suffix = null )
    {
        // The VersionChange that has been computed may be None.
        // On "+fake" version, we honor this "None": the target version is the "+fake" version (unchanged except the build metadata).
        // This allows a "v1.0.0+fake" to produce prereleases (like "v1.0.0-a") and/or ci builds (like "v1.0.0--ci.18")
        // until a non-ci build is done that will produce the "v1.0.0" version.
        // For regular base version, there's no "None": "Patch" is assumed.
        return vChange switch
        {
            SVersionChange.Major => thisVersion.Major == 0
                                    ? SVersion.Create( 0, thisVersion.Minor + 1, 0, suffix )
                                    : SVersion.Create( thisVersion.Major + 1, 0, 0, suffix ),
            SVersionChange.Minor => SVersion.Create( thisVersion.Major, thisVersion.Minor + 1, 0, suffix ),
            _ when thisVersion.HasFakeMetadata => SVersion.Create( thisVersion.Major, thisVersion.Minor, thisVersion.Patch, suffix ),
            _ => SVersion.Create( thisVersion.Major, thisVersion.Minor, thisVersion.Patch + 1, suffix )
        };
    }

    /// <summary>
    /// Computes the <see cref="SVersionChange"/> from this version to <paramref name="next"/>
    /// (this <see cref="SVersion.HasFakeMetadata"/> must be false otherwise a <see cref="InvalidOperationException"/> is thrown) 
    /// <para>
    /// The next version must also not be fake and it must a valid next version with a single increment
    /// of major, minor or patch (or have the same major, minor and patch) otherwise a <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="thisVersion">This version.</param>
    /// <param name="next">The next version.</param>
    /// <returns>The version change between this and the next one.</returns>
    public static SVersionChange FromNextVersion( this SVersion thisVersion, SVersion next )
    {
        Throw.CheckState( !thisVersion.HasFakeMetadata );
        Throw.CheckArgument( !next.HasFakeMetadata );
        Throw.CheckArgument( thisVersion <= next );

        SVersionChange c;
        if( thisVersion.Major == next.Major )
        {
            if( thisVersion.Minor == next.Minor )
            {
                Throw.CheckArgument( thisVersion == next || thisVersion.Patch == next.Patch - 1 );
                c = thisVersion.Patch == next.Patch
                        ? SVersionChange.None
                        : SVersionChange.Patch;
            }
            else
            {
                Throw.CheckArgument( thisVersion.Minor == next.Minor - 1 && next.Patch == 0 );
                c = SVersionChange.Minor;
            }
        }
        else
        {
            Throw.CheckArgument( thisVersion.Major == next.Major - 1 && next.Minor == 0 && next.Patch == 0 );
            c = SVersionChange.Major;
        }
        return c;
    }

}
