namespace CK.Env.NuGet
{
    /// <summary>
    /// Defines a NuGet feed, either remote or local.
    /// </summary>
    public interface INuGetRepository : IArtifactRepository
    {
        /// <summary>
        /// Gets the info of this feed.
        /// </summary>
        new INuGetRepositoryInfo Info { get; }
    }
}
