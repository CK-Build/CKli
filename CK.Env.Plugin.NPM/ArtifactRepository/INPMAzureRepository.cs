namespace CK.Env.NPM
{
    /// <summary>
    /// NPM azure repository.
    /// </summary>
    public interface INPMAzureRepository : INPMRepository
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
        /// Gets the project name of this Repository. Can be null.
        /// </summary>
        string ProjectName { get; }

    }
}
