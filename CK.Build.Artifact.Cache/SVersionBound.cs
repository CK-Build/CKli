using CSemVer;

namespace CK.Build
{
    /// <summary>
    /// This aims to define a sensible response to one of the dependency management issue: how to specify
    /// "version ranges". Each packet manager has its own way of doing it and this simple enum must be
    /// both "effective" and "common enough".
    /// <para>
    /// The behavior described here is the same between npm and NuGet except for prerelease tags: npm handles
    /// this is two different ways based on the <c>includePrerelease</c> flag. The default for this option has
    /// been mapped to this <see cref="FixedPatch"/> option.
    /// </para>
    /// <list type="bullet">
    ///     <item>npm: https://github.com/npm/node-semver. </item>
    ///     <item>NuGet: https://docs.microsoft.com/en-us/nuget/concepts/package-versioning#version-ranges. </item>
    /// </list>
    /// </summary>
    public enum SVersionBound
    {
        /// <summary>
        /// Version can be freely upgraded, no restriction apply.
        /// <para>
        /// For npm, this corresponds to the a '>=' prefix (when the version is naked, '=' applies and or us,
        /// it is <see cref="Fixed"/>).
        /// </para>
        /// <para>
        /// For NuGet, this corresponds to a naked version (Minimum version, inclusive): "1.2.3" is like the npm's ">=1.2.3".
        /// </para>
        /// </summary>
        None,

        /// <summary>
        /// Fixed version. Such fixed versions should be changed manually: it indicates that, for any reason, there is a
        /// strict adherence to the dependency's version.
        /// <para>
        /// For npm, this is the default for naked version: the '=' prefix is considered ("1.2.3" is like "=1.2.3").
        /// </para>
        /// <para>
        /// For NuGet, this is "Exact version match" denoted by brackets: "[1.2.3]".
        /// </para>
        /// </summary>
        Fixed,

        /// <summary>
        /// Same as <see cref="Fixed"/> for stable releases but when the version has a
        /// non empty <see cref="SVersion.Prerelease"/>, this allows greater pre-releases to match.
        /// <para>
        /// For npm, this is de facto the option when the version has a prerelease tag.
        /// See https://github.com/npm/node-semver#prerelease-tags.
        /// </para>
        /// <para>
        /// For NuGet, this can be specified with the "Mixed inclusive minimum and exclusive maximum version"
        /// with the trick of the lowest prerelease tag being "0" to avoid accepting prerelease of the next patch!
        /// syntax: "[1.2.3,1.2.4-0)" (the general pattern being "[X.Y.Z,X.Y.Z+1-0)").
        /// </para>
        /// </summary>
        FixedPatch,

        /// <summary>
        /// Allows Patch-level changes.
        /// <para>
        /// This is the ~ (Tilde Ranges) of npm version range specification when at least Major.Minor
        /// are specified: "~1.2.3" matches "1.2.3" to "<1.3.0".
        /// When only the Major is specified (like in "~2"), it is a <see cref="FixedMajor"/>.
        /// </para>
        /// <para>
        /// For NuGet, this can be specified with the "Mixed inclusive minimum and exclusive maximum version"
        /// with the trick of the lowest prerelease tag being "0" to avoid accepting prerelease of the next minor!
        /// syntax: "[1.2.3,1.3.0-0)" (the general pattern being "[X.Y.Z,X.Y+1.0-0)").
        /// </para>
        /// </summary>
        FixedMinor,

        /// <summary>
        /// Allows Minor and/or Patch-level changes.
        /// <para>
        /// This is the ^ (Caret Ranges) of npm version range specification when the Major is at
        /// least 1: "^2.3.4" matches "2.3.4" to "<3.0.0". When Major is 0, npm handle the ^ specifically:
        /// 0.0.x versions are <see cref="FixedPatch"/> and 0.x.y versions (where x >= 1) are <see cref="FixedMinor"/>.
        /// </para>
        /// <para>
        /// For NuGet, this can be specified with the "Mixed inclusive minimum and exclusive maximum version"
        /// with the trick of the lowest prerelease tag being "0" to avoid accepting prerelease of the next major!
        /// syntax: "[1.2.3,2.0.0-0)" (the general pattern being "[X.Y.Z,X+1.0.0-0)").
        /// </para>
        /// </summary>
        FixedMajor
    }
}
