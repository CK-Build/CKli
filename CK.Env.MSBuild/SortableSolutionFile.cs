using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.MSBuild
{
    public class SortableSolutionFile : IDependentItemContainer
    {
        readonly SolutionFile _solution;
        readonly IEnumerable<DependencyContext.ProjectItem> _projects;

        internal SortableSolutionFile( SolutionFile f, IEnumerable<DependencyContext.ProjectItem> projects )
        {
            _solution = f;
            _projects = projects;
        }

        public SolutionFile Solution => _solution;

        public IEnumerable<Project> Projects => _projects.Select( p => p.Project );

        string IDependentItem.FullName => _solution.UniqueSolutionName;

        IDependentItemContainerRef IDependentItem.Container => _solution.PrimarySolution;

        IEnumerable<IDependentItemRef> IDependentItemGroup.Children => _projects;

        IDependentItemRef IDependentItem.Generalization => null;

        IEnumerable<IDependentItemRef> IDependentItem.Requires => null;

        IEnumerable<IDependentItemRef> IDependentItem.RequiredBy => null;

        IEnumerable<IDependentItemGroupRef> IDependentItem.Groups => null;

        object IDependentItem.StartDependencySort( IActivityMonitor m ) => _solution;
    }
}
