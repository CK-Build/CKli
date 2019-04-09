namespace CK.Env.MSBuild
{
    /// <summary>
    /// Describes a project in a <see cref="DependencyAnalyser"/> context.
    /// </summary>
    public interface IDotNetDependentProject : IDependentProject
    {
        /// <summary>
        /// Gets the project itself.
        /// </summary>
        Project Project { get; }
    }
}
