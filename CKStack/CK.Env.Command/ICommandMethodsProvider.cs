
using CK.Core;

namespace CK.Env
{
    public interface ICommandMethodsProvider
    {
        /// <summary>
        /// Gets the provider name. This is the prefix of all the commands supported.
        /// This must be stable.
        /// </summary>
        NormalizedPath CommandProviderName { get; }
    }
}
