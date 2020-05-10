using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Core
{
    /// <summary>
    /// Basic mutable POCO used to describe a new package in a <see cref="PackageDB"/>.
    /// </summary>
    public class FullPackageInfo : PackageInfo, IFullPackageInfo
    {
        /// <summary>
        /// Gets the name of the feeds that are known to contain this package.
        /// </summary>
        public List<string> FeedNames { get; } = new List<string>();

        IEnumerable<string> IFullPackageInfo.FeedNames => FeedNames;
    }
}
