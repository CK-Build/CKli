using CKli.VersionTag.Plugin;

namespace CKli.Publish.Plugin;

public sealed partial class PublishRoadmap
{
    /// <summary>
    /// Captures an indirect required publication: the <see cref="Origin"/> is a "local/" <see cref="BuildSolution.LastBuild"/>
    /// that is not on the <see cref="HotGraph.BranchName"/> and the <see cref="Required"/> is also a "local/".
    /// Required can be the Origin itself, one of its consumer or a producer.
    /// </summary>
    /// <param name="Origin">The initially referenced release by the roadmap's pivots.</param>
    /// <param name="Required">The "local/" Repo/Version that must be published.</param>
    public readonly record struct RequiredPublish( RepoReleaseInfo Origin, RepoReleaseInfo Required )
    {
        /// <summary>
        /// Gets whether the <see cref="Required"/> is a producer of this <see cref="Origin"/>.
        /// If it's not a producer that it can be a consumer or the Origin itself.
        /// </summary>
        public bool IsProducer => Origin.AllProducers.Contains( Required );
    }

}

