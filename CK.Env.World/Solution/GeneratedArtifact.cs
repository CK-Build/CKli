namespace CK.Env
{
    /// <summary>
    /// Captures a <see cref="Artifact"/> with its path.
    /// </summary>
    public readonly struct GeneratedArtifact
    {
        /// <summary>
        /// Gets the artifact (type and name).
        /// </summary>
        public Artifact Artifact { get; }

        /// <summary>
        /// Gets the name.
        /// </summary>
        public string Name => Artifact.Name;

        /// <summary>
        /// Gets the folder path of this project relative to the primary solution folder.
        /// </summary>
        public string PrimarySolutionRelativeFolderPath { get; }

        /// <summary>
        /// Initializes a new <see cref="GeneratedArtifact"/>.
        /// </summary>
        /// <param name="a">The artifact.</param>
        /// <param name="path">Path of the package.</param>
        public GeneratedArtifact( Artifact a, string path )
        {
            Artifact = a;
            PrimarySolutionRelativeFolderPath = path;
        }
    }
}
