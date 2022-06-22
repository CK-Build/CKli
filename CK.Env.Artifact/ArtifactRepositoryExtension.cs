using CK.Core;
using CK.Build;

namespace CK.Env
{
    /// <summary>
    /// Extension methods.
    /// </summary>
    public static class ArtifactRepositoryExtension
    {
        /// <summary>
        /// Checks whether this artifact is accepted by this repository:
        /// <see cref="IArtifactRepository.HandleArtifactType(in ArtifactType)"/> and <see cref="IArtifactRepositoryInfo.QualityFilter"/>
        /// must be fine.
        /// </summary>
        /// <param name="this">This repository.</param>
        /// <param name="a">The artifact.</param>
        /// <returns>True if the artifact should be in the repository.</returns>
        public static bool Accepts( this IArtifactRepository @this, in ArtifactInstance a )
        {
            return a.Artifact.Type != null
                    ? @this.HandleArtifactType( a.Artifact.Type ) && @this.QualityFilter.Accepts( a.Version.PackageQuality )
                    : false;
        }
    }
}
