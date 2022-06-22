using CK.Core;
using System.Collections.Generic;

namespace CK.Build.PackageDB
{

    /// <summary>
    /// Captures changes from one database to a new one.
    /// </summary>
    public class ChangedInfo
    {
        /// <summary>
        /// Gets the new database or the current one if nothing changed.
        /// </summary>
        public PackageDatabase DB { get; }

        /// <summary>
        /// Gets the new database.
        /// </summary>
        public bool HasChanged { get; }

        /// <summary>
        /// Gets the set of <see cref="PackageChangedInfo"/>.
        /// </summary>
        public IReadOnlyList<PackageChangedInfo> PackageChanges { get; }

        /// <summary>
        /// Gets the feeds that appeared.
        /// </summary>
        public IReadOnlyList<PackageFeed> NewFeeds { get; }

        /// <summary>
        /// Gets the <see cref="FeedChangedInfo"/> for feeds that changed.
        /// </summary>
        public IReadOnlyList<FeedChangedInfo> FeedChanges { get; }

        internal ChangedInfo( PackageDatabase newDB,
                              bool hasChanged,
                              IReadOnlyList<PackageChangedInfo> packageChanged,
                              IReadOnlyList<PackageFeed> newFeeds,
                              IReadOnlyList<FeedChangedInfo> feedChanges )
        {
            Throw.CheckNotNullArgument( newDB );
            Throw.CheckNotNullArgument( packageChanged );
            Throw.CheckNotNullArgument( newFeeds );
            Throw.CheckNotNullArgument( feedChanges );
            DB = newDB;
            HasChanged = hasChanged;
            PackageChanges = packageChanged;
            NewFeeds = newFeeds;
            FeedChanges = feedChanges;
        }
    }

}

