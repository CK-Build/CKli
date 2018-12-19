using System;
using System.Collections.Generic;
using System.Text;

namespace CK.NuGetClient
{
    /// <summary>
    /// Implements comparison of <see cref="INuGetFeedInfo"/> based on <see cref="INuGetFeedInfo.Name"/>.
    /// </summary>
    public class NuGetFeedInfoComparer : IEqualityComparer<INuGetFeedInfo>, IComparer<INuGetFeedInfo>
    {
        int IComparer<INuGetFeedInfo>.Compare( INuGetFeedInfo x, INuGetFeedInfo y ) => StringComparer.Ordinal.Compare( x?.Name, y?.Name );

        bool IEqualityComparer<INuGetFeedInfo>.Equals( INuGetFeedInfo x, INuGetFeedInfo y ) => x?.Name == y?.Name;

        int IEqualityComparer<INuGetFeedInfo>.GetHashCode( INuGetFeedInfo obj ) => obj.Name.GetHashCode();

        /// <summary>
        /// Gets the single equality comparer and comparer.
        /// </summary>
        public static readonly NuGetFeedInfoComparer Default = new NuGetFeedInfoComparer();
    }
}
