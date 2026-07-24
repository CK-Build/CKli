using CK.Core;
using NuGet.Protocol.Core.Types;
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
    /// Adds an error log on failure of <see cref="SVersion.IsPreviousVersionNumbersOf(SVersion, out SVersionChange)"/>.
    /// </summary>
    /// <param name="thisVersion">This version.</param>
    /// <param name="monitor">The monitor to signal a failure.</param>
    /// <param name="next">The next version.</param>
    /// <param name="change">The version change between this and the next one.</param>
    /// <returns>
    /// True if next follows this version with the <paramref name="change"/> (that can be <see cref="SVersionChange.None"/>
    /// if the Major, Minor and Patch numbers are equal), false otherwise.
    /// </returns>
    public static bool IsPreviousVersionNumbersOf( this SVersion thisVersion, IActivityMonitor monitor, SVersion next, out SVersionChange change )
    {
        if( !thisVersion.IsPreviousVersionNumbersOf( next, out change ) )
        {
            monitor.Error( ActivityMonitor.Tags.ToBeInvestigated, $"The version '{next}' doesn't follow version '{thisVersion}'." );
            return false;
        }
        return true;
    }

}
