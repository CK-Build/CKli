namespace CK.Env
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
        public DependentProject Project { get; }

        /// <summary>
        /// Initializes a new <see cref="GeneratedArtifact"/>.
        /// </summary>
        /// <param name="a">The artifact.</param>
        /// <param name="project">The project.</param>
        public GeneratedArtifact( Artifact a, DependentProject project )
        {
            Artifact = a;
            Project = project;
        }
    }
}
