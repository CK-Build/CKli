using CK.Core;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Extends <see cref="IArtifactFeed"/> with NuGet specific properties
    /// and specific capabilities.
    /// </summary>
    public interface INuGetFeed : IArtifactFeed
    {
        /// <summary>
        /// Gets the feed name. Can not be null.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the feed url. Can not be null.
        /// </summary>
        string Url { get; }

        /// <summary>
        /// Gets optional credentials for the source. Can be null.
        /// </summary>
        SimpleCredentials Credentials { get; }

    }
}
