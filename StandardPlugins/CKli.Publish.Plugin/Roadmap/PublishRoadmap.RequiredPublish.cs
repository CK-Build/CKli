using CKli.VersionTag.Plugin;
using System;

namespace CKli.Publish.Plugin;

sealed partial class PublishRoadmap
{
    [Obsolete("Superseded by PublishedPackageInfo.")]
    /// <summary>
    /// Captures a required publication: the <see cref="Origin"/> is a "local/" <see cref="Build.Plugin.Roadmap.BuildSolution.LastBuild"/>
    /// - not on the <see cref="HotGraph.BranchName"/> - or the <see cref="Build.Plugin.Roadmap.BuildInfo.TargetVersion"/> and
    /// the <see cref="Required"/> is also a "local/".
    /// <para>
    /// Required can be the Origin itself, one of its consumer or a producer.
    /// </para>
    /// </summary>
    /// <param name="Origin">The initially referenced release by the roadmap's pivots.</param>
    /// <param name="Required">The "local/" Repo/Version that must be published.</param>
    public readonly record struct RequiredPublish( RepoReleaseInfo Origin, RepoReleaseInfo Required )
    {
        /// <summary>
        /// Gets whether the <see cref="Required"/> is a producer of this <see cref="Origin"/>.
        /// If it's not a producer then it can be a consumer or the Origin itself.
        /// </summary>
        public bool IsProducer => Origin.AllProducers.Contains( Required );
    }

}

