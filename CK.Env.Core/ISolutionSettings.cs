using CK.NuGetClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public interface ISolutionSettings
    {
        /// <summary>
        /// Gets whether the NuGet.config file must exist.
        /// </summary>
        bool SuppressNuGetConfigFile { get; }

         /// <summary>
        /// Gets whether the solution produces CKSetup components.
        /// </summary>
        bool ProduceCKSetupComponents { get; }

       /// <summary>
        /// Gets whether source link is disabled.
        /// Impacts Common/Shared.props file.
        /// </summary>
        bool DisableSourceLink { get; }

        /// <summary>
        /// Defines the set of NuGet sources that is used.
        /// Impacts NuGet.config file.
        /// </summary>
        IReadOnlyCollection<INuGetSource> NuGetSources { get; }

        /// <summary>
        /// Gets the NuGet source names that must be excluded.
        /// Must be used to clean up existing source names that must no more be used.
        /// Impacts NuGet.config file.
        /// </summary>
        IReadOnlyCollection<string> ExcludedNuGetSourceNames { get; }

        /// <summary>
        /// Gets the NuGet feeds where produced packages must be pushed.
        /// </summary>
        IReadOnlyCollection<INuGetFeedInfo> NuGetPushFeeds { get; }

        /// <summary>
        /// Gets the NuGet push feed names that must be excluded.
        /// </summary>
        IReadOnlyCollection<string> ExcludedNuGetPushFeedNames { get; }
    }
}
