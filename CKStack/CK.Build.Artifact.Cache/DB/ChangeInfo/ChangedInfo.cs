using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CK.Build.PackageDB
{

    /// <summary>
    /// Captures changes from one database to a new one.
    /// </summary>
    public sealed class ChangedInfo
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
        /// Gets the feeds that have been removed.
        /// </summary>
        public IReadOnlyList<PackageFeed> DroppedFeeds { get; }

        /// <summary>
        /// Gets the <see cref="FeedChangedInfo"/> for feeds that changed.
        /// </summary>
        public IReadOnlyList<FeedChangedInfo> FeedChanges { get; }

        internal ChangedInfo( PackageDatabase noChange )
            : this( noChange, false, Array.Empty<PackageChangedInfo>(), Array.Empty<PackageFeed>(), Array.Empty<FeedChangedInfo>(), Array.Empty<PackageFeed>() )
        {
        }

        internal ChangedInfo( PackageDatabase newDB,
                              bool hasChanged,
                              IReadOnlyList<PackageChangedInfo> packageChanged,
                              IReadOnlyList<PackageFeed> newFeeds,
                              IReadOnlyList<FeedChangedInfo> feedChanges,
                              IReadOnlyList<PackageFeed> droppedFeeds )
        {
            Debug.Assert( newDB != null );
            Debug.Assert( packageChanged != null );
            Debug.Assert( newFeeds != null );
            Debug.Assert( feedChanges != null );
            Debug.Assert( droppedFeeds != null );
            DB = newDB;
            HasChanged = hasChanged;
            PackageChanges = packageChanged;
            NewFeeds = newFeeds;
            FeedChanges = feedChanges;
            DroppedFeeds = droppedFeeds;
        }
    }

}

