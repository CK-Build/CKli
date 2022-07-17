using CK.Build.PackageDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Build
{
    /// <summary>
    /// Defines the <see cref="IPackageFeed.FeedPackageInfoObtained"/> event argument.
    /// </summary>
    public sealed class RawPackageInfoEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new RawPackageInfoEventArgs.
        /// </summary>
        /// <param name="info">The package instance info.</param>
        /// <param name="rawInfo">The raw data.</param>
        public RawPackageInfoEventArgs( IPackageInstanceInfo info, object rawInfo )
        {
            Info = info;
            RawInfo = rawInfo;
        }

        /// <summary>
        /// Gets the package info that has been read.
        /// </summary>
        public IPackageInstanceInfo Info { get; }

        /// <summary>
        /// Gets the raw data that has been obtained from the feed.
        /// The object's type totally depends on the feed: it may be a <see cref="System.Text.Json.JsonDocument"/>,
        /// <see cref="System.Text.Json.JsonElement"/>, a <see cref="System.Xml.XmlElement"/> or even
        /// a string or a binary blob.
        /// <para>
        /// In all case, it must obviously not be mutated in any way.
        /// </para>
        /// </summary>
        public object RawInfo { get; }
    }
}
