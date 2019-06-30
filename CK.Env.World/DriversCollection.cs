using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public class DriversCollection
    {
        readonly HashSet<ISolutionDriver> _solutionDrivers;
        readonly WorldState _worldState;

        internal DriversCollection(WorldState worldState)
        {
            _worldState = worldState;
            _solutionDrivers = new HashSet<ISolutionDriver>();
        }

        //public IEnumerable<ISolutionDriver> GetSolutionDriversOn( string branchName ) => _solutionDrivers.Where( p => p.GitRepository.CurrentBranchName == branchName );
        public IEnumerable<ISolutionDriver> GetDriversOnCurrentBranch( IActivityMonitor m )
        {
            return _worldState.GetSolutionDependencyContextOnCurrentBranches( m ).Drivers;
        }

        public IEnumerable<ISolutionDriver> AllDrivers => _solutionDrivers;

        public IEnumerable<ISolutionDriver> GetDriversOnBranch( string branchName ) => _solutionDrivers.Where( p => p.BranchName == branchName );

        public int Count => _solutionDrivers.Count;

        public bool Add( ISolutionDriver item ) =>  _solutionDrivers.Add( item );

        public void Clear()
        {
            _solutionDrivers.Clear();
        }

        public bool Contains( ISolutionDriver item )
        {
            return _solutionDrivers.Contains( item );
        }

        public void CopyTo( ISolutionDriver[] array, int arrayIndex )
        {
            _solutionDrivers.CopyTo( array, arrayIndex );
        }

        public bool Remove( ISolutionDriver item ) => _solutionDrivers.Remove( item );
    }
}
