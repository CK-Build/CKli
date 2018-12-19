using CK.Env;
using System;
using System.Net.Http;

namespace CK.NuGetClient
{
    /// <summary>
    /// Encapsulates a NuGet feed manager.
    /// </summary>
    public interface INuGetClient : IArtifactTypeFactory
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
        /// Finds an existing feed.
        /// </summary>
        /// <param name="name">The feed name.</param>
        /// <returns>The feed or null if not found.</returns>
        INuGetFeed Find( string name );

        /// <summary>
        /// Creates a NuGet feed from its description.
        /// If this feed already exists, an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="info">The feed description.</param>
        /// <returns>The feed.</returns>
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
