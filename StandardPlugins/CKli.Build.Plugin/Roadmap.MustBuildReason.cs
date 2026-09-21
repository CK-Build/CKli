using CKli.HotZone.Plugin;
using System;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Qualifies the reason to build a repository.
    /// </summary>
    [Flags]
    public enum MustBuildReason
    {
        /// <summary>
        /// No build required.
        /// </summary>
        None = 0,

        /// <summary>
        /// An upstream repository must be built.
        /// </summary>
        UpstreamBuild = 1,

        /// <summary>
        /// An upstream repository has been built, the solution must be built.
        /// </summary>
        UpstreamVersion = 2,

        /// <summary>
        /// The <see cref="HotGraph.SolutionVersionInfo.GetLastBuild(bool)"/> has a "+fake" version.
        /// </summary>
        FakeVersion = 4,

        /// <summary>
        /// The <see cref="HotGraph.SolutionVersionInfo.GetLastBuild(bool)"/> has a "+deprecated" version.
        /// </summary>
        DeprecatedVersion = 8,

        /// <summary>
        /// One or more package dependencies must be updated.
        /// </summary>
        DependencyUpdate = 16,

        /// <summary>
        /// Code changed.
        /// </summary>
        CodeChange = 32,

        /// <summary>
        /// There is no code change, "--ci.0" (<see cref="CIBuildMode.CIForce"/>) is used and the last
        /// version is a PUBLISHED non-CI build: the commit must be rebuilt in CI, which opens a new
        /// version line above the published one. See <see cref="RollingLocal"/> for the local case.
        /// </summary>
        CI0 = 64,

        /// <summary>
        /// There is no code change, a CI build is done (the default mode is enough) and the last version
        /// is a non-CI build that is still a pending <c>local/</c> (or <c>building/</c>) release: the CI
        /// build takes its place on the same commit and the pending release is destroyed.
        /// <para>
        /// This is the "rolling local build" that <c>TagCommit.CanBearVersion</c> already sanctions: nothing
        /// consumed that release, so there is no version line to protect and no reason to require "--ci.0".
        /// It is what makes a regular build run by mistake recoverable with the obvious command.
        /// </para>
        /// </summary>
        RollingLocal = 128,
    }

}

