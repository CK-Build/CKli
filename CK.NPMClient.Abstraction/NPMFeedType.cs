namespace CK.NPMClient
{
    /// <summary>
    /// Dscribes NPM feed type that we handle.
    /// </summary>
    public enum NPMFeedType
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
