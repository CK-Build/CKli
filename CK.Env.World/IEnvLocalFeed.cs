using CK.Core;
using CK.NuGetClient;
using CK.Text;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Defines a simple local folder.
    /// </summary>
    public interface IEnvLocalFeed
    {
        /// <summary>
        /// Gets the logical branch name that corresponds to this folder.
        /// </summary>
        StandardGitStatus LogicalBranchName { get; }

        /// <summary>
        /// Gets the physical path of this local feed.
        /// </summary>
        NormalizedPath PhysicalPath { get; }

        /// <summary>
        /// Gets the best version for a package.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <returns>The version or null if not found.</returns>
        SVersion GetBestVersion( IActivityMonitor m, string packageId );

        /// <summary>
        /// Gets a package or null if not found.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <param name="v">The package version.</param>
        /// <returns>The local package file or null if not found.</returns>
        LocalNuGetPackageFile GetPackageFile( IActivityMonitor m, string packageId, SVersion v );

        /// <summary>
        /// Gets all package files.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The package files.</returns>
        IEnumerable<LocalNuGetPackageFile> GetAllPackageFiles( IActivityMonitor m );

        /// <summary>
        /// Removes a specific version of a package.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package's version.</param>
        void Remove( IActivityMonitor m, string packageId, SVersion version );

    }
}
