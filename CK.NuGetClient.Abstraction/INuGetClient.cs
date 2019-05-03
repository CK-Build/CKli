using CK.Env;
using System.Net.Http;

namespace CK.NuGetClient
{
    /// <summary>
    /// Encapsulates a NuGet feed manager.
    /// </summary>
    public interface INuGetClient : IArtifactRepositoryFactory
    {
        /// <summary>
        /// Gets the shared <see cref="HttpClient"/> that will be used for remote access.
        /// </summary>
        HttpClient HttpClient { get; }

        /// <summary>
        /// Gets the key store.
        /// </summary>
        ISecretKeyStore SecretKeyStore { get; }
 
        /// <summary>
        /// Finds or creates a feed.
        /// If a feed with the same <see cref="INuGetFeedInfo.Name"/> exists,
        /// it is returned.
        /// </summary>
        /// <param name="info">The feed info.</param>
        /// <returns>The new or existing feed.</returns>
        INuGetFeed FindOrCreate( INuGetFeedInfo info );
    }
}
