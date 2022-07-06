namespace CK.Env
{
    /// <summary>
    /// Extends <see cref="IArtifactFeed"/> with NuGet specific properties
    /// and specific capabilities.
    /// </summary>
    public interface INuGetFeed : IArtifactFeed
    {
        /// <summary>
        /// Gets the feed url. Can not be null.
        /// </summary>
        string Url { get; }

        /// <summary>
        /// Gets optional credentials for the source.
        /// </summary>
        SimpleCredentials? Credentials { get; }

        /// <summary>
        /// Gets whether the full package information must be required
        /// instead of the lightest RemoteSourceDependencyInfo.
        /// Defaults to false.
        /// </summary>
        bool UseFullInformation { get; set; }

    }
}
