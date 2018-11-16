using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
