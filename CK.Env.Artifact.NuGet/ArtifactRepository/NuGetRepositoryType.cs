namespace CK.Env.NuGet
{
    /// <summary>
    /// Describes NuGet feed type that we handle.
    /// </summary>
    public enum NuGetRepositoryType
    {
        /// <summary>
        /// Not applicable.
        /// </summary>
        None,

        /// <summary>
        /// Standard NuGet feed.
        /// </summary>
        NuGetStandard,

        /// <summary>
        /// Azure DevOps feed.
        /// </summary>
        NuGetAzure
    }
}
