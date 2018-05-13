using CK.Core;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{

    public interface ILocalFeedProvider
    {
        /// <summary>
        /// Ensures that the LocalFeed/CI physically available folder exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The LocalFeed/CI directory info.</returns>
        IFileInfo GetCIFeedFolder( IActivityMonitor m );

        /// <summary>
        /// Ensures that the LocalFeed/Local physically available folder exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The LocalFeed/Local directory info.</returns>
        IFileInfo GetLocalFeedFolder( IActivityMonitor m );

        /// <summary>
        /// Ensures that the LocalFeed/Release physically available folder exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The LocalFeed/Release directory info.</returns>
        IFileInfo GetReleaseFeedFolder( IActivityMonitor m );

        /// <summary>
        /// Finds a package in a specific version in the local feeds.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The exact version.</param>
        /// <returns>True if the package exists.</returns>
        bool FindInAnyLocalFeeds( IActivityMonitor m, string packageId, SVersion version );

        /// <summary>
        /// Gets the version for a package across LocalFeed/Local, LocalFeed/CI and LocalFeed/Release feeds.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <returns>The version or null if not found.</returns>
        SVersion GetBestAnyLocalVersion( IActivityMonitor m, string packageId );

        /// <summary>
        /// Gets the version for a package in LocalFeed/CI.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <returns>The version or null if not found.</returns>
        SVersion GetBestLocalCIVersion( IActivityMonitor m, string packageId );


        /// <summary>
        /// Gets all package files in LocalFeed/Release folder.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="withSymbols">True to return .symbols.nupkg files.</param>
        /// <returns>The package files.</returns>
        IEnumerable<LocalPackageFile> GetAllPackageFilesInReleaseFeed( IActivityMonitor m, bool withSymbols = false );

        LocalPackageFile GetBestLocalCIPackage( IActivityMonitor m );

        LocalPackageFile GetLocalCIPackage( IActivityMonitor m, string packageId, SVersion v );

        /// <summary>
        /// Removes a specific version of a package from the local NuGet cache.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package's version.</param>
        void RemoveFromNuGetCache( IActivityMonitor m, string packageId, SVersion version );

        /// <summary>
        /// Gets the nuget.exe directory to use.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The full directory path where nuget.exe can be found or null.</returns>
        string GetNuGetCommandLineDirectory( IActivityMonitor m );

    }
}
