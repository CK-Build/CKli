using CK.Env.DependencyModel;

namespace CK.Env
{
    /// <summary>
    /// View of the World from a Solution driver.
    /// </summary>
    public interface ISolutionDriverWorld
    {
        /// <summary>
        /// Gets the world name.
        /// </summary>
        IRootedWorldName WorldName { get; }

        /// <summary>
        /// Gets the global work status.
        /// Null when the world is not initialized.
        /// </summary>
        GlobalWorkStatus? WorkStatus { get; }

        /// <summary>
        /// Registers a new driver and provides him the <see cref="SolutionContext"/> to use.
        /// </summary>
        /// <param name="driver">The driver.</param>
        /// <returns>The solution context into which the solution must be created.</returns>
        SolutionContext Register( ISolutionDriver driver );

        /// <summary>
        /// Unregister a previously registered driver.
        /// </summary>
        /// <param name="driver">The driver.</param>
        void Unregister( ISolutionDriver driver );
    }
}
