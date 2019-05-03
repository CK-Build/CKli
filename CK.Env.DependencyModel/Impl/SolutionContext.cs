using CK.Core;
using CK.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Solution context handles a set of solutions and their projects.
    /// </summary>
    public class SolutionContext : IReadOnlyCollection<Solution>, ISolutionContext
    {
        readonly Dictionary<string, Solution> _solutions;
        readonly List<Project> _allProjects;
        int _version;
        DependencyAnalyzer _analyzer;

        class ProjectNameComparer : IComparer<Project>
        {
            public static readonly ProjectNameComparer Comparer = new ProjectNameComparer();

            public int Compare( Project x, Project y )
            {
                int cmp = x.SimpleProjectName.CompareTo( y.SimpleProjectName );
                if( cmp != 0 ) return cmp;
                cmp = x.Solution.Name.CompareTo( y.Solution.Name );
                if( cmp != 0 ) return cmp;
                cmp = x.Type.CompareTo( y.Type );
                if( cmp != 0 ) return cmp;
                return x.FullFolderPath.CompareTo( y.FullFolderPath );
            }
        }

        /// <summary>
        /// Initializes a new context.
        /// </summary>
        public SolutionContext()
        {
            _solutions = new Dictionary<string, Solution>();
            _allProjects = new List<Project>();
        }

        internal bool OnProjectAdding( Project newOne )
        {
            int idx = _allProjects.BinarySearch( newOne, ProjectNameComparer.Comparer );
            if( idx >= 0 ) return false;
            idx = ~idx;
            _allProjects.Insert( idx, newOne );
            HandleHomonymsAround( newOne.SimpleProjectName, idx, false );
            return true;
        }

        void HandleHomonymsAround( string simpleName, int idx, bool removed )
        {
            int idxB = idx - 1;
            int idxA = removed ? idx : idx + 1;
            while( idxB >= 0 && _allProjects[idxB].SimpleProjectName == simpleName ) --idxB;
            while( idxA < _allProjects.Count && _allProjects[idxA].SimpleProjectName == simpleName ) ++idxA;
            int nbHomonym = idxA - idxB - 1;
            var h = _allProjects.GetRange( idxB + 1, nbHomonym );
            foreach( var p in h ) p.NormalizeName( h );
        }

        /// <summary>
        /// Creates a new solution on a logical path and a name that must both be unique.
        /// </summary>
        /// <param name="fullPath">The unique path of the solution.</param>
        /// <param name="name">The unique name of the solution.</param>
        public Solution AddSolution( NormalizedPath fullPath, string uniqueName )
        {
            var s = new Solution( this, fullPath, uniqueName );
            _solutions.Add( fullPath, s );
            _solutions.Add( uniqueName, s );
            OnSolutionAdded( s );
            return s;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Gets the number of solutions.
        /// </summary>
        public int Count => _solutions.Count;

        /// <summary>
        /// Gets the current version. This changes each time
        /// anything changes in the solutions or projects.
        /// </summary>
        public int Version => _version;

        /// <summary>
        /// Gets the solution by its name or by its <see cref="Solution.FullPath"/>.
        /// </summary>
        /// <param name="key">The solution name or full path.</param>
        /// <returns>The solution.</returns>
        public Solution this[string key] => _solutions[key];

        /// <summary>
        /// Returns an enumerator on the solutions.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public IEnumerator<Solution> GetEnumerator() => _solutions.Values.GetEnumerator();

        /// <summary>
        /// Gets a <see cref="DependencyAnalyzer"/> that is up to date (based on the <see cref="Version"/>).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The up to date dependency context.</returns>
        public DependencyAnalyzer GetDependencyAnalyser( IActivityMonitor m )
        {
            if( _analyzer == null || _analyzer.IsObsolete )
            {
                _analyzer = DependencyAnalyzer.Create( m, this );
            }
            return _analyzer;
        }

        IEnumerator<ISolution> IEnumerable<ISolution>.GetEnumerator() => _solutions.Values.GetEnumerator();

        internal void OnSolutionAdded( Solution s )
        {
            ++_version;
        }


        internal void OnProjectAdded( Project p )
        {
            ++_version;
        }

        internal void OnProjectRemoved( Project p )
        {
            int idx = _allProjects.BinarySearch( p, ProjectNameComparer.Comparer );
            Debug.Assert( idx >= 0 );
            _allProjects.RemoveAt( idx );
            HandleHomonymsAround( p.SimpleProjectName, idx, true );
            ++_version;
        }

        internal void OnBuildProjectChanged( Solution solution )
        {
            ++_version;
        }

        internal void OnIsTestProjectChanged( Project project )
        {
            ++_version;
        }

        internal void OnIsPublishedChange( Project project )
        {
            ++_version;
        }

        internal void OnArtifactAdded( Artifact a, Project project )
        {
            ++_version;
        }

        internal void OnArtifactRemoved( Artifact a, Project project )
        {
            ++_version;
        }

        internal void OnPackageReferenceRemoved( PackageReference r )
        {
            ++_version;
        }

        internal void OnPackageReferenceAdded( PackageReference r )
        {
            ++_version;
        }

        internal void OnProjectReferenceAdded( ProjectReference r )
        {
            ++_version;
        }

        internal void OnProjectReferenceRemoved( ProjectReference r )
        {
            ++_version;
        }
    }
}
