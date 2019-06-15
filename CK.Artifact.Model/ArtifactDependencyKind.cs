namespace CK.Core
{
    /// <summary>
    /// Defines how a project, package, any artifact references another project, package or any artifact.
    /// </summary>
    public enum ArtifactDependencyKind
    {
        /// <summary>
        /// Non applicable.
        /// </summary>
        None,

        /// <summary>
        /// Non transitive dependency.
        /// </summary>
        Private,

        /// <summary>
        /// Development only dependency. No transitivity at all.
        /// </summary>
        Development,

        /// <summary>
        /// "Normal", standard, transitive dependency.
        /// This is the "Peer dependency" of NPM, see https://lexi-lambda.github.io/blog/2016/08/24/understanding-the-npm-dependency-model/ 
        /// </summary>
        Transitive
    }
    
}
