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
