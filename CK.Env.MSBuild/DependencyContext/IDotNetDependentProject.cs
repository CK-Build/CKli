namespace CK.Env.MSBuild
{
    /// <summary>
    /// Describes a .Net project (actually a .csproj) in
    /// a <see cref="DependencyAnalyser"/> context.
    /// </summary>
    public interface IDotNetDependentProject : IDependentProject
    {
        /// <summary>
        /// Gets the project itself.
        /// </summary>
        IP Project { get; }
    }
}
