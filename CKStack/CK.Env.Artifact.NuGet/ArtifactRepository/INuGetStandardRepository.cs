namespace CK.Env.NuGet
{
    /// <summary>
    /// A NuGet standard repository adds an Url to the repository name.
    /// </summary>
    public interface INuGetStandardRepository : INuGetRepository
    {
        /// <summary>
        /// Gets the NuGet repository url.
        /// </summary>
        string Url { get; }
    }
}
