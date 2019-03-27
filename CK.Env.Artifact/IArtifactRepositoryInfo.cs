namespace CK.Env
{
    /// <summary>
    /// Defines common repository information.
    /// Name of the repository and the secret key name depend on the actual repository type. 
    /// </summary>
    public interface IArtifactRepositoryInfo
    {
        /// <summary>
        /// Gets the unique name of this repository.
        /// It should uniquely identify the repository in any context and may contain type, address, urls, or any information
        /// that helps defining unicity.
        /// <para>
        /// This name depends on the repository type. When described externally in xml, the "CheckName" attribute when it exists
        /// must be exactly this computed name.
        /// </para>
        /// </summary>
        string UniqueArtifactRepositoryName { get; }

        /// <summary>
        /// Must provide the secret key name.
        /// A null or empty SecretKeyName means that the repository does not require any protection.
        /// </summary>
        string SecretKeyName { get; }
    }
}
