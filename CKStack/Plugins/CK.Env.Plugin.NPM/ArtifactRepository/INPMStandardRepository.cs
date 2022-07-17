namespace CK.Env.NPM
{
    /// <summary>
    /// NuGet standard repository.
    /// </summary>
    public interface INPMStandardRepository : INPMRepository
    {
        /// <summary>
        /// Gets the NPM registry url.
        /// </summary>
        string Url { get; }

        /// <summary>
        /// True if the registry uses password instead of Paersonal Access Tokens.
        /// </summary>
        bool UsePassword { get; }
    }
}
