using CK.Core;
using CK.NuGetClient;
using CK.Text;
using CSemVer;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Defines a simple local folder.
    /// </summary>
    public interface IEnvLocalFeed
    {
        /// <summary>
        /// Gets the physical path of this local feed.
        /// </summary>
        NormalizedPath PhysicalPath { get; }

        /// <summary>
        /// Gets the best version for a NuGet package.
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
        /// Checks any missing artifact instances in this feed.
        /// </summary>
        /// <param name="target">The repository target.</param>
        /// <param name="artifacts">Set of expected artifact instances.</param>
        /// <returns>A non null list with the missing artifacts if any.</returns>
        List<ArtifactInstance> GetMissing( IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts );

        /// <summary>
        /// Removes artifact instances from this feed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifacts">The artifacts to remove.</param>
        void Remove( IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts );

        /// <summary>
        /// Locates all instances of the required artifacts and pushes them to the given target.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="target">The repository target.</param>
        /// <param name="artifacts">Set of artifact instances.</param>
        /// <returns>True on success, false on error.</returns>
        bool PushLocalArtifacts( IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts );

        /// <summary>
        /// Clears all local artifacts.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        bool RemoveAll( IActivityMonitor m );
    }
}
