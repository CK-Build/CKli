using CK.Core;
using CSemVer;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    /// <summary>
    /// Defines a NPM feed, either remote or local.
    /// </summary>
    public interface INPMArtifactRepository : IArtifactRepository
    {
        /// <summary>
        /// Gets the info of this feed.
        /// </summary>
        new INPMArtifactRepositoryInfo Info { get; }

        /// <summary>
        /// Cheks whether a versioned package exists in this feed.
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="v">The version.</param>
        /// <returns>True if found, false otherwise.</returns>
        Task<bool> ExistsAsync( IActivityMonitor m, string packageId, SVersion v );
    }
}
