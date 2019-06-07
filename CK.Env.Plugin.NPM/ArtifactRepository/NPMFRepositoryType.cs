namespace CK.Env.NPM
{
    /// <summary>
    /// Describes NPM feed type that we handle.
    /// </summary>
    public enum NPMFRepositoryType
    {
        /// <summary>
        /// Not applicable.
        /// </summary>
        None,

        /// <summary>
        /// Standard NPM feed.
        /// </summary>
        NPMStandard,

        /// <summary>
        /// Azure DevOps feed.
        /// </summary>
        NPMAzure
    }
}
