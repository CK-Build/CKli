using System.Collections.Generic;

namespace CK.Build.PackageDB
{
    /// <summary>
    /// Describes changes in a feed.
    /// </summary>
    public readonly struct FeedChangedInfo
    {
        /// <summary>
        /// Gets the feed that has changed.
        /// </summary>
        public PackageFeed Feed { get; }

        /// <summary>
        /// Gets the packages that have been added to the feed.
        /// </summary>
        public IReadOnlyList<PackageInstance> AddedPackages { get; }

        /// <summary>
        /// Gets the packages that no longer appear in this feed.
        /// </summary>
        public IReadOnlyList<PackageInstance> RemovedPackages { get; }

        internal FeedChangedInfo( PackageFeed feed, IReadOnlyList<PackageInstance> added, IReadOnlyList<PackageInstance> removed )
        {
            Feed = feed;
            AddedPackages = added;
            RemovedPackages = removed;
        }
    }

}

