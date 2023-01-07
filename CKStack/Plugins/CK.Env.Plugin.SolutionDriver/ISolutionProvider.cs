using CK.Core;
using CK.Env.DependencyModel;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Provides a way for other plugins to synchronize the logical (dependency Model) <see cref="ISolutionDriver"/>'s
    /// <see cref="Solution"/> exposed by <see cref="ISolutionDriver.GetSolution(IActivityMonitor, bool, bool)"/>.
    /// A provider must synchronize the projects, package references and generated artifacts.
    /// Once the providers have been called:
    /// <list type="bullet">
    /// <item>
    /// The <see cref="ISolution.ArtifactSources"/> are synchronized based on <see cref="ISolution.AllPackageReferences"/>.
    /// </item>
    /// <item>
    /// The <see cref="ISolution.ArtifactTargets"/> are synchronized based on <see cref="ISolution.GeneratedArtifacts"/>.
    /// </item>
    /// <item>
    /// The <see cref="SolutionDriver.OnSolutionConfiguration"/> event is raised.
    /// </item>
    /// </list>
    /// <para>
    /// Providers must be registered by calling <see cref="SolutionDriver.RegisterSolutionProvider(ISolutionProvider)"/>.
    /// </para>
    /// </summary>
    public interface ISolutionProvider
    {
        /// <summary>
        /// Gets whether this provider needs to resynchronize the driver's solution.
        /// </summary>
        bool IsDirty { get; }

        /// <summary>
        /// Forces this provider to resynchronize.
        /// This is to be called whenever, for any reason, the current file system may have changed
        /// (a <see cref="GitRepository.ResetHard(IActivityMonitor)"/> occurred for instance).
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        void SetDirty( IActivityMonitor monitor );

        /// <summary>
        /// Must synchronize the <see cref="SolutionConfigurationEventArgs.Solution"/>.
        /// On error, <see cref="SolutionConfigurationEventArgs.PreventSolutionUse(string)"/> should be called.
        /// </summary>
        /// <param name="sender">The sender (the driver).</param>
        /// <param name="e">The event that expose the solution and build secrets that may be declared or provided.</param>
        void ConfigureSolution( object? sender, SolutionConfigurationEventArgs e );
    }
}
