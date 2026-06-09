using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;

namespace CKli;

/// <summary>
/// Extends <see cref="SVersion"/>.
/// </summary>
public static class SVersionExtensions
{
    /// <summary>
    /// Gets whether the version is a "-local.fix." prerelease.
    /// </summary>
    /// <param name="version">This version.</param>
    /// <returns>True if this is a "-local.fix." version.</returns>
    public static bool IsLocalFix( this SVersion version ) => version.IsPrerelease && version.Prerelease.StartsWith( "local.fix.", StringComparison.Ordinal );

    /// <summary>
    /// Gets whether <see cref="SVersion.BuildMetaData"/> is "fake".
    /// </summary>
    /// <param name="version">This version.</param>
    /// <returns>True if this is a "+fake" version.</returns>
    public static bool IsFake( this SVersion version ) => version.BuildMetaData.Equals( "fake", StringComparison.Ordinal );

    /// <summary>
    /// Gets whether <see cref="SVersion.ParsedPrefix"/> is "local/".
    /// </summary>
    /// <param name="version">This version.</param>
    /// <returns>True if this is a "local/" prefixed version.</returns>
    public static bool IsLocal( this SVersion version ) => version.ParsedPrefix.AsSpan().Equals( "local/", StringComparison.Ordinal );

    /// <summary>
    /// Gets whether <see cref="SVersion.BuildMetaData"/> is "fake" and ensures that the <paramref name="normalized"/> fake version
    /// has no prerelease part.
    /// </summary>
    /// <param name="version">This version.</param>
    /// <param name="normalized">The normalized fake version: a fake version is a stable version.</param>
    /// <returns>True if this is a "+fake" version.</returns>
    public static bool IsFake( this SVersion version, [NotNullWhen(true)]out SVersion? normalized )
    {
        if( version.BuildMetaData.Equals( "fake", StringComparison.Ordinal ) )
        {
            // We use Parse here to have a non null ParsedText.
            normalized = version.IsPrerelease
                            ? SVersion.ParseNoThrow( $"{version.ParsedPrefix}v{version.Major}.{version.Minor}.{version.Patch}+fake", checkBuildMetaDataSyntax: false )
                            : version;
            return true;
        }
        normalized = null;
        return false;
    }

    /// <summary>
    /// Gets whether this version is a "rough base" of the target. This version MUST be <see cref="SVersion.IsStable"/> otherwise
    /// an <see cref="InvalidOperationException"/> is thrown. The target is roughly based on this version if it has
    /// the same Major.Minor.Patch or any valid increment (Major+1.0.0, Major.Minor+1.0 or Major.Minor.Patch+1).
    /// <para>
    /// This accepts any prerelease of this version and any version that immediately follows this version, including their prerelease,
    /// so this accepts any "post release" of this version (with the double dash trick).
    /// </para>
    /// <para>
    /// This is used by the fake version (see <see cref="IsFake(SVersion, out SVersion?)"/>):
    /// <list type="bullet">
    ///     <item>
    ///         For CI build versions: "1.0.0" is a rough base of "1.0.0--ci.1" (that is
    ///         an artificial CI build version that is used only for fake versions) and real CI build like 1.0.1--ci.0, 1.1.0--ci.4 or 2.0.0--ci.4.
    ///     </item>
    ///     <item>
    ///         For regular versions: "1.0.0" is a rough base of itself, of any of its prelease versions like "1.0.0-a", of its successors "1.0.1",
    ///         "1.1.0", "2.0.0" and any of their pre releases.
    ///     </item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="version">This stable version.</param>
    /// <param name="target">The target that may be roughly based on this stable version.</param>
    /// <returns></returns>
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
