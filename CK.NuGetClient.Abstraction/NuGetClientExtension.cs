using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.NuGetClient
{
    public static class NuGetClientExtension
    {

        /// <summary>
        /// Attempts to resolve required secrets for a set of <see cref="INuGetFeedInfo"/>.
        /// If a secret can not be resolved, it will appear as null in the result list.
        /// </summary>
        /// <param name="this">This NuGet client.</param>
        /// <param name="m">The monitor to use.</param>
        /// <param name="feedInfos">The set of feed info for which secrets must be resolved.</param>
        /// <returns>
        /// The list of resolved secrets: a null secret means that the secret has not been successfully obtained
        /// for the corresponding SecretKeyName.
        /// </returns>
        public static IReadOnlyList<(string SecretKeyName, string Secret)> ResolveSecrets( this INuGetClient @this, IActivityMonitor m, IEnumerable<INuGetFeedInfo> feedInfos )
        {
            return feedInfos.Select( info => @this.FindOrCreate( info ) )
                                    .Distinct()
                                    .Where( feed => !String.IsNullOrWhiteSpace( feed.SecretKeyName ) )
                                    .GroupBy( feed => feed.SecretKeyName )
                                    .Select( g => (g.Key, Secret: g.First().ResolveSecret( m )) )
                                    .ToList();
        }
    }
}
