using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Build
{
    /// <summary>
    /// Basic mutable POCO used to describe a new package in a <see cref="PackageDB"/>.
    /// </summary>
    public class FullPackageInfo : PackageInfo, IFullPackageInfo
    {
        /// <summary>
        /// Gets or sets whether the <see cref="FeedNames"/> are all the feeds that contain this package.
        /// When false, feed names are only a subset of the feeds that contain this package.
        /// </summary>
        public bool AllFeedNamesAreKnown { get; set; }

        /// <summary>
        /// Gets the name of the feeds that are known to contain this package.
        /// This can be empty: a package can exist in a <see cref="PackageDB"/> without being in any <see cref="PackageFeed"/>.
        /// </summary>
        public List<string> FeedNames { get; } = new List<string>();

        IEnumerable<string> IFullPackageInfo.FeedNames => FeedNames;
    }
}
