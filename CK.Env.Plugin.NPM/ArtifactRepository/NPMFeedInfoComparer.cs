using System;
using System.Collections.Generic;

namespace CK.Env.NPM
{
    /// <summary>
    /// Implements comparison of <see cref="INPMFeedInfo"/> based on <see cref="INPMFeedInfo.Name"/>.
    /// </summary>
    public class NPMFeedInfoComparer : IEqualityComparer<INPMFeedInfo>, IComparer<INPMFeedInfo>
    {
        int IComparer<INPMFeedInfo>.Compare( INPMFeedInfo x, INPMFeedInfo y ) => StringComparer.Ordinal.Compare( x?.Name, y?.Name );

        bool IEqualityComparer<INPMFeedInfo>.Equals( INPMFeedInfo x, INPMFeedInfo y ) => x?.Name == y?.Name;

        int IEqualityComparer<INPMFeedInfo>.GetHashCode( INPMFeedInfo obj ) => obj.Name.GetHashCode();

        /// <summary>
        /// Gets the single equality comparer and comparer.
        /// </summary>
        public static readonly NPMFeedInfoComparer Default = new NPMFeedInfoComparer();
    }
}
