using System;
using System.Collections.Generic;
using System.Text;

namespace CK.NuGetClient
{
    /// <summary>
    /// Generalizes different type of feed descriptions.
    /// Only <see cref="Name"/> appears. Url or source is hidden since
    /// some feed implementation may not have one (composite feed for instance).
    /// </summary>
    public interface INuGetFeedInfo
    {
        /// <summary>
        /// Gets the type of feed.
        /// </summary>
        NuGetFeedType Type { get; }

        /// <summary>
        /// Gets the name of this feed.
        /// Name is used as the feed identifier: it must be unique accross a set of feeds.
        /// (See <see cref="NuGetFeedInfoComparer"/>.)
        /// </summary>
        string Name { get; }
    }
}
