using System;
using System.Collections.Generic;
using System.Text;

namespace CK.NuGetClient
{
    /// <summary>
    /// Dscribes NuGet feed type that we handle.
    /// </summary>
    public enum NuGetFeedType
    {
        /// <summary>
        /// Not applicable.
        /// </summary>
        None,

        /// <summary>
        /// Standard NuGet feed.
        /// </summary>
        NuGetStandard,

        /// <summary>
        /// Azure DevOps feed.
        /// </summary>
        NuGetAzure
    }
}
