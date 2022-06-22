using System.Collections.Generic;

namespace CK.Build.PackageDB
{
    /// <summary>
    /// Describes changes in a feed.
    /// Both <see cref="AddedPackages"/> and <see cref="RemovedPackages"/> can be empty
    /// if only package updates occurred (<see cref="PackageEventType.ContentOnlyChanged"/> or <see cref="PackageEventType.StateOnlyChanged"/>).
    /// </summary>
    public readonly struct FeedChangedInfo
    {
        /// <summary>
        /// Gets the feed that has changed (the new instance).
        /// </summary>
        public PackageFeed Feed { get; }

        /// <summary>
        /// Gets the packages that have been added to the feed.
        /// </summary>
        public IReadOnlyList<PackageInstance> AddedPackages { get; }

        /// <summary>
        /// Gets the packages that no longer appear in this feed (these are the instances from the
        /// previous database).
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

