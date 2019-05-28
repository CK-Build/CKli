using CK.Text;
using System.Collections.Generic;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Captures a <see cref="Artifact"/> with the project that creates it.
    /// </summary>
    public readonly struct GeneratedArtifact
    {
        /// <summary>
        /// Gets the artifact (type and name).
        /// </summary>
        public Artifact Artifact { get; }

        /// <summary>
        /// Gets the project that generates this artifact.
        /// </summary>
        public IProject Project { get; }

        /// <summary>
        /// Gets a set of full paths (folder or files) that are "sources" for this artifact.
        /// By default, <see cref="IProject.ProjectSources"/> is returned but this may be an independent
        /// set if required.
        /// </summary>
        IReadOnlyCollection<NormalizedPath> ArtifactSources { get; }

        /// <summary>
        /// Initializes a new <see cref="GeneratedArtifact"/>.
        /// </summary>
        /// <param name="a">The artifact.</param>
        /// <param name="project">The project.</param>
        /// <param name="sources">Optional explicit sources that differ from <see cref="IProject.ProjectSources"/>.</param>
        public GeneratedArtifact( Artifact a, IProject project, IReadOnlyCollection<NormalizedPath> sources = null )
        {
            Artifact = a;
            Project = project;
            ArtifactSources = sources ?? project.ProjectSources;
        }

        public override string ToString() => $"{Project}->{Artifact}";
    }
}
