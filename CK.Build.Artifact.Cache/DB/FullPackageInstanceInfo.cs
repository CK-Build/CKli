using CK.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Build
{
    /// <summary>
    /// Basic mutable POCO used to describe a new package in a <see cref="PackageDB"/>.
    /// </summary>
    public class FullPackageInstanceInfo : PackageInstanceInfo, IFullPackageInfo
    {
        /// <inheritdoc />
        public bool AllFeedNamesAreKnown { get; set; }

        /// <inheritdoc cref="IFullPackageInfo.FeedNames" />
        public List<string> FeedNames { get; } = new List<string>();

        IEnumerable<string> IFullPackageInfo.FeedNames => FeedNames;
    }
}
