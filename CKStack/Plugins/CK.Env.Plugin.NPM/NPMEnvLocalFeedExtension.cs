using CK.Core;
using CK.Env.NodeSln;
using CK.Env.NPM;
using CSemVer;
using System.Collections.Generic;

namespace CK.Env
{
    public static class NPMEnvLocalFeedExtension
    {
        /// <summary>
        /// Gets a <see cref="LocalNPMPackageFile"/> or null if not found.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <param name="v">The package version.</param>
        /// <returns>The local package file or null if not found.</returns>
        public static LocalNPMPackageFile? GetNPMPackageFile( this IEnvLocalFeed @this, IActivityMonitor m, string packageId, SVersion v )
        {
            var f = NodeProjectDependency.CreateTarballPath( @this.PhysicalPath, packageId, v );
            return System.IO.File.Exists( f ) ? new LocalNPMPackageFile( f, packageId, v ) : null;
        }

    }
}
