

namespace CK.Env
{
    /// <summary>
    /// Defines the Git host CKli knows and handles.
    /// </summary>
    public enum KnownGitProvider
    {
        /// <summary>
        /// Unknown Git provider: <see cref="GitRepositoryKey"/> cannot compute
        /// its <see cref="GitRepositoryKey.ReadPATKeyName"/> and <see cref="GitRepositoryKey.WritePATKeyName"/>.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The GitHub site.
        /// </summary>
        GitHub = 1,

        /// <summary>
        /// The GitHub site.
        /// </summary>
        GitLab = 2,

        /// <summary>
        /// Azure Dev Ops infrastructure.
        /// </summary>
        AzureDevOps = 3,

        /// <summary>
        /// BitBucket site.
        /// </summary>
        Bitbucket = 4
    }

}
