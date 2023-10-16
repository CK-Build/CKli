using CK.Core;
using CK.Env.DependencyModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Handles drivers per branches and their respective solutions.
    /// </summary>
    public sealed class DriversCollection
    {
        internal sealed class WorldBranchContext : IWorldSolutionContext
        {
            readonly List<ISolutionDriver> _drivers;
            readonly SolutionContext? _context;
            readonly PairList _pairList;
            SolutionDependencyContext? _depContext;

            /// <summary>
            /// Initializes an empty context for a given branch.
            /// </summary>
            /// <param name="branchName">The branch name.</param>
            internal WorldBranchContext( string branchName )
            {
                BranchName = branchName;
                _context = new SolutionContext();
                _drivers = new List<ISolutionDriver>();
                _pairList = new PairList( this );
            }

            /// <summary>
            /// Initializes an empty context for "current" branches: the drivers are the one of the
            /// develop branch (<see cref="BranchName"/> is null).
            /// </summary>
            /// <param name="drivers">The drivers for each branch.</param>
            internal WorldBranchContext( IEnumerable<ISolutionDriver> drivers )
            {
                _drivers = drivers.ToList();
                _pairList = new PairList( this );
            }

            /// <summary>
            /// Gets the branch name.
            /// This is null for "current" branches context.
            /// </summary>
            public string? BranchName { get; }

            SolutionDependencyContext IWorldSolutionContext.DependencyContext => _depContext!;

            IReadOnlyList<DependentSolution> IWorldSolutionContext.DependentSolutions => _depContext!.Solutions;

            public IReadOnlyList<ISolutionDriver> Drivers => _drivers;

            sealed class PairList : IReadOnlyList<(DependentSolution Solution, ISolutionDriver Driver)>
            {
                readonly WorldBranchContext _c;

                public PairList( WorldBranchContext c ) => _c = c;

                public (DependentSolution Solution, ISolutionDriver Driver) this[int index] => (_c._depContext!.Solutions[index], _c._drivers[index]);

                public int Count => _c._drivers.Count;

                public IEnumerator<(DependentSolution Solution, ISolutionDriver Driver)> GetEnumerator()
                {
                    return _c._depContext!.Solutions.Select( s => (s, _c._drivers[s.Index]) ).GetEnumerator();
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public IReadOnlyList<(DependentSolution Solution, ISolutionDriver Driver)> Solutions => _pairList;

            public IWorldSolutionContext? Refresh( IActivityMonitor m, bool forceReload )
            {
                Throw.NotSupportedException();
                return this;
            }

            internal SolutionContext OnRegisterDriver( ISolutionDriver d )
            {
                Debug.Assert( d != null && !_drivers.Contains( d ) );
                Debug.Assert( _context != null, "This is called only on real context, not 'current branches' one." );
                _drivers.Add( d );
                _depContext = null;
                return _context;
            }

            internal void OnUnregisterDriver( ISolutionDriver d )
            {
                _drivers.Remove( d );
                _depContext = null;
            }
        }

        readonly HashSet<ISolutionDriver> _solutionDrivers;
        readonly Dictionary<string, WorldBranchContext> _perBranchContext;

        internal DriversCollection()
        {
            _solutionDrivers = new HashSet<ISolutionDriver>();
            _perBranchContext = new Dictionary<string, WorldBranchContext>();
        }

        /// <summary>
        /// Gets the <see cref="WorldBranchContext"/> of a branch.
        /// Return null if there is no <see cref="WorldBranchContext"/> for the branch.
        /// </summary>
        /// <param name="branchName">The name of the branch</param>
        /// <returns>The <see cref="WorldBranchContext"/> of a branch or null if it doesn't exist.</returns>
        internal WorldBranchContext? GetContextOnBranch( string branchName )
        {
            return _perBranchContext.TryGetValue( branchName, out var ctx ) ? ctx : null;
        }

        /// <summary>
        /// Gets an up to date <see cref="IWorldSolutionContext"/> for the current branches.
        /// Each drivers can be in different branches.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reloadSolutions">True to force a reload of the solutions.</param>
        /// <returns>The context or null on error.</returns>
        public IWorldSolutionContext? GetSolutionDependencyContextOnCurrentBranches( IActivityMonitor monitor, bool reloadSolutions = false )
        {
            var currentDrivers = GetDriversOnCurrentBranch();
            var c = new WorldBranchContext( currentDrivers );
            return c.Refresh( monitor, reloadSolutions );
        }

        /// <summary>
        /// Gets all the drivers, through all the Git folder branches, that have been registered so far.
        /// </summary>
        public IReadOnlyCollection<ISolutionDriver> AllDrivers => _solutionDrivers;

        /// <summary>
        /// Gets the solution drivers for the currently checked out branches.
        /// </summary>
        /// <returns>The set of drivers.</returns>
        public IEnumerable<ISolutionDriver> GetDriversOnCurrentBranch()
        {
            var dedup = new HashSet<ISolutionDriver>();
            var drivers = _solutionDrivers.ToArray();
            foreach( var d in drivers )
            {
                dedup.Add( d.GetCurrentBranchDriver() );
            }
            return dedup;
        }

        /// <summary>
        /// Gets all the drivers for all repositories in one specific branch.
        /// </summary>
        /// <param name="branchName">The branch name to filter.</param>
        /// <returns>The set of drivers for the branch.</returns>
        public IEnumerable<ISolutionDriver> GetDriversOnBranch( string branchName ) => _solutionDrivers.Where( p => p.BranchName == branchName );

        /// <summary>
        /// Enlists a new driver in the root HashSet, ensures that a <see cref="WorldBranchContext"/> exists
        /// for the <see cref="ISolutionDriver.BranchName"/> and returns it.
        /// </summary>
        /// <param name="driver">The driver to register.</param>
        /// <returns>The WorldBranchContext into which the driver has been registered.</returns>
        internal WorldBranchContext Register( ISolutionDriver driver )
        {
            if( !_perBranchContext.TryGetValue( driver.BranchName, out var c ) )
            {
                c = new WorldBranchContext( driver.BranchName );
                _perBranchContext.Add( driver.BranchName, c );
            }
            Throw.CheckState( "Already registered.", _solutionDrivers.Add( driver ) );
            return c;
        }

        /// <summary>
        /// Removes a driver that must exist (otherwise an exception is thrown).
        /// </summary>
        /// <param name="driver">The driver to remove.</param>
        internal void Unregister( ISolutionDriver driver )
        {
            Throw.CheckState( "Not registered.", _solutionDrivers.Remove( driver ) );
            _perBranchContext[driver.BranchName].OnUnregisterDriver( driver );
        }
    }
}
