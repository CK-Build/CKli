namespace CK.Env.NuGet
{
    /// <summary>
    /// NuGet standard repository.
    /// </summary>
    public interface INuGetStandardRepository : INuGetRepository
    {
        /// <summary>
        /// Gets the NuGet repository url.
        /// </summary>
        string Url { get; }
    }
}
