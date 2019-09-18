namespace CK.Env.NuGet
{
    /// <summary>
    /// NuGet azure repository.
    /// The name of this repository is <see cref="Organization"/>-<see cref="FeedName"/>[-<see cref="Label"/>(without the '@' label prefix)].
    /// </summary>
    public interface INuGetAzureRepository : INuGetRepository
    {
        /// <summary>
        /// Gets the organization name.
        /// </summary>
        string Organization { get; }

        /// <summary>
        /// Gets the name of the feed inside the <see cref="Organization"/>.
        /// </summary>
        string FeedName { get; }

        /// <summary>
        /// Gets the "@Label" string or null.
        /// </summary>
        string Label { get; }
    }
}
