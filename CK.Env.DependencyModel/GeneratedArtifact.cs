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
        /// Gets the folder path of this project relative to the primary solution folder.
        /// </summary>
        public IProject Project { get; }

        /// <summary>
        /// Initializes a new <see cref="GeneratedArtifact"/>.
        /// </summary>
        /// <param name="a">The artifact.</param>
        /// <param name="project">The project.</param>
        public GeneratedArtifact( Artifact a, IProject project )
        {
            Artifact = a;
            Project = project;
        }
    }
}
