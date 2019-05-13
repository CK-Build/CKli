namespace CK.Env
{
    /// <summary>
    /// Defines a NPM source (the .npmrc registry).
    /// </summary>
    public interface INPMSource
    {
        /// <summary>
        /// Gets the scope. Can not be null and must start with a '@'.
        /// </summary>
        string Scope { get; }

        /// <summary>
        /// Gets the registry url. Can not be null.
        /// </summary>
        string Url { get; }

        /// <summary>
        /// Gets optional credentials for the source.
        /// Can be null.
        /// </summary>
        SimpleCredentials Credentials { get; }

    }
}
