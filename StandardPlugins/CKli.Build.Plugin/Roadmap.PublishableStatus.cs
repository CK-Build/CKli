using CKli.HotZone.Plugin;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Describes the state of a <see cref="BuildSolution"/> regarding its publication.
    /// <para>
    /// It may not be obvious but the order of these statuses matters: a "logical combination" that summarizes
    /// the statuses of multiple solutions is achieved by considering the maximum value.
    /// </para>
    /// </summary>
    internal enum PublishableStatus
    {
        /// <summary>
        /// Not relevant: the <see cref="BuildSolution"/> is not in the scope of the <see cref="Roadmap.Pivots"/>.
        /// <see cref="BuildSolution.BuildInfo"/> is null.
        /// </summary>
        None = 0,

        /// <summary>
        /// The solution is an upstream repository and its current version is already published.
        /// The current version may be from the <see cref="HotGraph.BranchName"/> or not.
        /// </summary>
        AlreadyPublished = 1,

        /// <summary>
        /// The solution is already locally built (in the <see cref="HotGraph.BranchName"/>) by a previous build.
        /// It must also be published.
        /// </summary>
        PublishRequired = 2,

        /// <summary>
        /// The solution can be published because <see cref="BuildSolution.MustBuild"/> is true: the <see cref="BuildInfo.TargetVersion"/>
        /// will be publishable.
        /// </summary>
        Build = 3,

        /// <summary>
        /// The solution is an upstream repository that has been locally built in a base branch (not in
        /// the current <see cref="HotGraph.BranchName"/>).
        /// This must be published along with all the repositories that consume this version.
        /// </summary>
        PublishRequiredBaseBranch = 4,

        /// <summary>
        /// The solution is an upstream repository (not built in the current build) and the current version has been produced
        /// by a failing build (typically because one of its downstream repositories failed to build).
        /// <para>
        /// This must be fixed before publishing the current build.
        /// The current version may be from the <see cref="HotGraph.BranchName"/> or not.
        /// </para>
        /// </summary>
        BuildingPending = 5
    }
}
