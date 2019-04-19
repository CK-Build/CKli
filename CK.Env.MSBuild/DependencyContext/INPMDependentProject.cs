namespace CK.Env.MSBuild
{
    /// <summary>
    /// Describes a NPM project in a <see cref="DependencyAnalyser"/> context.
    /// </summary>
    public interface INPMDependentProject : IDependentProject
    {
        /// <summary>
        /// Gets the project itself.
        /// </summary>
        NPMProject Project { get; }
    }
}
