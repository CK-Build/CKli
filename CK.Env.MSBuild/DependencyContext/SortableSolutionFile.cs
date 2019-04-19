using CK.Core;
using CK.Setup;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.MSBuild
{
    internal class SortableSolutionFile : IDependentItemContainer
    {
        readonly Solution _solution;
        IEnumerable<IDependentItemRef> _projects;

        internal SortableSolutionFile( Solution f, IEnumerable<IDependentItemRef> projects )
        {
            _solution = f;
            _projects = projects;
        }

        public Solution Solution => _solution;

        internal void AddSecondaryProjects( IEnumerable<IDependentItemRef> secondaryProjects )
        {
            _projects = _projects.Concat( secondaryProjects );
        }

        string IDependentItem.FullName => _solution.UniqueSolutionName;

        IDependentItemContainerRef IDependentItem.Container => _solution.SpecialType == SolutionSpecialType.IndependantSecondarySolution
                                                                ? null
                                                                : _solution.PrimarySolution;

        IEnumerable<IDependentItemRef> IDependentItemGroup.Children => _projects;

        IDependentItemRef IDependentItem.Generalization => null;

        IEnumerable<IDependentItemRef> IDependentItem.Requires => null;

        IEnumerable<IDependentItemRef> IDependentItem.RequiredBy => null;

        IEnumerable<IDependentItemGroupRef> IDependentItem.Groups => null;

        object IDependentItem.StartDependencySort( IActivityMonitor m ) => _solution;

    }
}
