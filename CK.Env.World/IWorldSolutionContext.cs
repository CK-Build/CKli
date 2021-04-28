using CK.Core;
using CK.Env.DependencyModel;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Captures a set of <see cref="Solution"/> and their respective <see cref="ISolutionDriver"/>
    /// that is able to refresh/reload the solutions.
    /// In practice a <see cref="World"/> maintains a context per logical branch it works on ('develop'
    /// and/or 'local') however this may be used to work on solutions accross different branches as long
    /// as there is no duplicate solution in the context (see <see cref="Solution.Name"/>
    /// and <see cref="Solution.FullPath"/>).
    /// </summary>
    public interface IWorldSolutionContext
    {
        /// <summary>
        /// Gets the <see cref="SolutionDependencyContext"/>.
        /// This contains the resolved <see cref="DependentSolutions"/> and
        /// the whole solution model.
        /// </summary>
        SolutionDependencyContext DependencyContext { get; }

        /// <summary>
        /// Gets the global, sorted, dependencies informations between solutions.
        /// (This is just a shortcut to <see cref="SolutionDependencyContext.Solutions"/> property).
        /// </summary>
        IReadOnlyList<DependentSolution> DependentSolutions { get; }

        /// <summary>
        /// Gets the drivers ordered like the <see cref="Solutions"/>.
        /// </summary>
        IReadOnlyList<ISolutionDriver> Drivers { get; }

        /// <summary>
        /// Gets the <see cref="DependentSolutions"/> associated to their respective driver
        /// ordered by the <see cref="DependentSolution.Index"/>.
        /// </summary>
        IReadOnlyList<(DependentSolution Solution, ISolutionDriver Driver)> Solutions { get; }

        /// <summary>
        /// Ensures that this context is up to date, optionnally fully reloading the solutions.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="forceReload">True to force the reload of all the solutions.</param>
        /// <returns>An updated context or null on error.</returns>
        IWorldSolutionContext? Refresh( IActivityMonitor m, bool forceReload );
    }


    public static class WorldSolutionContextExtension
    {
        /// <summary>
        /// Finds the driver that handles a project or returns null if not found.
        /// </summary>
        /// <param name="p">The project for which the dirver must be obtained.</param>
        /// <returns>The driver of null if not found.</returns>
        public static ISolutionDriver? FindDriver( this IWorldSolutionContext @this, IProject p )
        {
            var idx = @this.DependencyContext[p.Solution]?.Index;
            return idx != null ? @this.Drivers[idx.Value] : null;
        }


    }
}
