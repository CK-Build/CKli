using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines the abstract view of any <see cref="ArtifactInstance"/> repository.
    /// </summary>
    public interface IArtifactRepository
    {
        /// <summary>
        /// Gets the info of this repository.
        /// </summary>
        IArtifactRepositoryInfo Info { get; }

        /// <summary>
        /// Ensures that the secret behind the <see cref="IArtifactRepositoryInfo.SecretKeyName"/> is available.
        /// The implementation must ensure that the secret only depends from the <see cref="SecretKeyName"/>:
        /// if two repositories share the same SecretKeyName, the resolved secret must be the same.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The non empty secret or null.</returns>
        string ResolveSecret( IActivityMonitor m, bool throwOnEmpty = false );

        /// <summary>
        /// Checks whether this artifact repository handles the artifact type.
        /// </summary>
        /// <param name="artifactType">Type of the artifact.</param>
        /// <returns>True if this repository can handle artifacts of this type, false otherwise.</returns>
        bool HandleArtifactType( string artifactType );

        /// <summary>
        /// Pushes/transfers one or more existing local artifacts into this repository.
        /// The concrete artifact set must be understandable by this repository otherwise an error must be logged
        /// and false must be returned.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifacts">The artifacts to push.</param>
        /// <returns>True on success, false on error.</returns>
        Task<bool> PushAsync( IActivityMonitor m, IArtifactLocalSet artifacts );
    }
}
