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
        /// Must provide the secret key name.
        /// A null or empty SecretKeyName means that the repository does not require any protection.
        /// </summary>
        string SecretKeyName { get; }

        /// <summary>
        /// Ensures that the secret behind the <see cref="SecretKeyName"/> is available.
        /// The implementation must ensure that the secret only depends from the <see cref="SecretKeyName"/>:
        /// if two repositories share the same SecretKeyName, the resolved secret must be the same.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The non empty secret or null.</returns>
        string ResolveSecret( IActivityMonitor m, bool throwOnEmpty = false );

        /// <summary>
        /// Finds an artifact instance in this repository and returns a locator or null.
        /// When found, this locator is not necessarily tied to this repository (but it can be).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="type">The artifact type.</param>
        /// <param name="name">The artifact name.</param>
        /// <param name="version">The artifact version.</param>
        /// <returns>A locator or null if not found.</returns>
        Task<IArtifactLocator> FindAsync( IActivityMonitor m, string type, string name, SVersion version );

        /// <summary>
        /// Pushes/transfers one or more existing artifacts into this repository.
        /// The artifact locators must be understandable by this repository otherwise an error must be logged
        /// and false must be returned.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifacts">The artifacts to push.</param>
        /// <returns>True on success, false on error.</returns>
        Task<bool> PushAsync( IActivityMonitor m, IEnumerable<IArtifactLocator> artifacts );
    }
}
