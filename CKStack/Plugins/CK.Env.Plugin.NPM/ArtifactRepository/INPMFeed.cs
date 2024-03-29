namespace CK.Env.NPM
{
    /// <summary>
    /// Extends <see cref="IArtifactFeed"/> with NPM specific properties
    /// and specific capabilities.
    /// </summary>
    public interface INPMFeed : IArtifactFeed
    {
        /// <summary>
        /// Gets the scope name. Cannot be null and must start with @.
        /// </summary>
        string Scope { get; }

        /// <summary>
        /// Gets the feed url. Cannot be null.
        /// </summary>
        string Url { get; }

        /// <summary>
        /// Gets optional credentials for the source. Can be null.
        /// </summary>
        SimpleCredentials Credentials { get; }
    }
}
