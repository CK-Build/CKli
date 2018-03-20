using CK.Setup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CK.Env.MSBuild
{
    public class SolutionDependencyResult
    {
        public class DependencyRow
        {
            /// <summary>
            /// Gets the build order for the <see cref="Solution"/>.
            /// </summary>
            public int Index { get; }

            /// <summary>
            /// Gets the solution file.
            /// </summary>
            public SolutionFile Solution { get; }

            /// <summary>
            /// Gets one of the projects that references the package produced by the <see cref="Target"/> project.
            /// Null if <see cref="Solution"/> does not require any other solution.
            /// </summary>
            public Project Origin { get; }

            /// <summary>
            /// Gets the target project.
            /// Null if <see cref="Solution"/> does not require any other solution.
            /// </summary>
            public Project Target { get; }

            internal DependencyRow( int idx, SolutionFile s, Project o, Project t )
            {
                Index = idx;
                Solution = s;
                Origin = o;
                Target = t;
            }

            public override string ToString()
            {
                return $"{Index}|{Solution.UniqueSolutionName}|{Origin.Name}|{Target.Name}";
            }
        }

        internal SolutionDependencyResult( SolutionSortStrategy c, IDependencySorterResult r )
        {
            Debug.Assert( r != null && !r.IsComplete );
            Content = c;
            RawSorterResult = r;
        }

        internal SolutionDependencyResult( SolutionSortStrategy c, IDependencySorterResult r, IReadOnlyList<DependencyRow> t )
        {
            Debug.Assert( r != null && r.IsComplete && t != null );
            Content = c;
            RawSorterResult = r;
            DependencyTable = t;
        }

        /// <summary>
        /// Gets the kind of projects that have been considered to sort solutions.
        /// </summary>
        public SolutionSortStrategy Content { get; }

        /// <summary>
        /// Gets the details of the depedencies between solutions.
        /// </summary>
        public IReadOnlyList<DependencyRow> DependencyTable { get; }

        /// <summary>
        /// Gets the <see cref="IDependencySorterResult"/>.
        /// </summary>
        public IDependencySorterResult RawSorterResult { get; }

        /// <summary>
        /// Gets whether solutions and their projects have been successfully ordered.
        /// </summary>
        public bool HasError => !RawSorterResult.IsComplete;


    }

}
