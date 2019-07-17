using CK.Core;
using CSemVer;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines the abstract view of any <see cref="ArtifactInstance"/> repository.
    /// </summary>
    public interface IArtifactRepository
    {
        /// <summary>
        /// Gets the unique name of this repository.
        /// It should uniquely identify the repository in any context and may contain type, address, urls, or any information
        /// that helps defining unicity.
        /// <para>
        /// This name depends on the repository type. When described externally in xml, the "CheckName" attribute when it exists
        /// must be exactly this computed name.
        /// </para>
        /// </summary>
        string UniqueRepositoryName { get; }

        /// <summary>
        /// Gets the range of package quality that is accepted by this feed.
        /// </summary>
        PackageQualityFilter QualityFilter { get; }

        /// <summary>
        /// Must provide the secret key name.
        /// A null or empty SecretKeyName means that the repository does not require any protection (or
        /// uses a different, external, security architecture).
        /// </summary>
        string SecretKeyName { get; }

        /// <summary>
        /// Ensures that the secret behind the <see cref="IArtifactRepositoryInfo.SecretKeyName"/> is available
        /// and if not, must return null.
        /// The implementation must ensure that the secret only depends from the <see cref="SecretKeyName"/>:
        /// if two repositories share the same SecretKeyName, the resolved secret must be the same.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The non empty secret or null.</returns>
        string ResolveSecret( IActivityMonitor m, bool throwOnEmpty = false );

        /// <summary>
        /// Gets whether this repository is ready to accept artifacts.
        /// This must be false if a <see cref="SecretKeyName"/> is defined  but
        /// can not be resolved.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Checks whether this artifact repository handles the artifact type.
        /// </summary>
        /// <param name="artifactType">Type of the artifact.</param>
        /// <returns>True if this repository can handle artifacts of this type, false otherwise.</returns>
        bool HandleArtifactType( in ArtifactType artifactType );

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
