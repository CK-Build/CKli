using System.Net.Http;

namespace CK.NuGetClient
{
    public interface INuGetClient
    {
        HttpClient HttpClient { get; }

        ISecretKeyStore SecretKeyStore { get; }

        INuGetFeed Find( string name );

        INuGetFeed Create( INuGetFeedInfo info );

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
