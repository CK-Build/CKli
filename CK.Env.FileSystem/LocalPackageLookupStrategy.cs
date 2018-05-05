using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines the only 3 possible strategies that we need to handle
    /// package upgrades accross repositories.
    /// </summary>
    public enum LocalPackageLookupStrategy
    {
        /// <summary>
        /// This can be used when globally on the 'develop' branch.
        /// </summary>
        LocalFeedOnly,

        /// <summary>
        /// This can be used when globally on the 'local' branch.
        /// </summary>
        BlankFeedOnly,

        /// <summary>
        /// This has to be used when transitioning from 'develop' to 'local' and vice-versa.
        /// </summary>
        BlankOrLocalFeed
    }

}
