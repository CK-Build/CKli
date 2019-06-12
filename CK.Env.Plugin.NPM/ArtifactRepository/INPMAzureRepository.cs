using CK.Core;
using CSemVer;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    /// <summary>
    /// NPM azure repository.
    /// </summary>
    public interface INPMAzureRepository : INPMRepository
    {
        /// <summary>
        /// Gets the organization name.
        /// </summary>
        string Organization { get; }

        /// <summary>
        /// Gets the name of the feed inside the <see cref="Organization"/>.
        /// </summary>
        string FeedName { get; }

    }
}
