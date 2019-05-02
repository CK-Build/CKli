//using CK.Core;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;

//namespace CK.Env.MSBuild
//{
//    /// <summary>
//    /// Internal cache for <see cref="IProjectFramework"/>.
//    /// </summary>
//    class ProjectFrameworkCache
//    {
//        readonly IReadOnlyList<ProjectDependencyResult.ProjectDepencyRow> _dependencies;
//        readonly Dictionary<(IDotNetDependentProject, CKTrait), Referencer> _referencers;

//        public ProjectFrameworkCache( IReadOnlyList<ProjectDependencyResult.ProjectDepencyRow> dependencies )
//        {
//            _dependencies = dependencies;
//            _referencers = new Dictionary<(IDotNetDependentProject, CKTrait), Referencer>();
//        }

//        public class Referencer : IProjectFramework
//        {
//            readonly HashSet<IProjectFramework> _allRefs;
//            IReadOnlyList<IProjectFramework> _refs;

//            public IDotNetDependentProject Project { get; }
//            public CKTrait Framework { get; }
//            public IReadOnlyList<IProjectFramework> Refs => _refs;
//            public int Depth { get; private set; }

//            public IReadOnlyCollection<IProjectFramework> AllRefs => _allRefs;

//            internal void SetRefs( List<Referencer> refs )
//            {
//                _refs = refs;
//                _allRefs.AddRange( refs.SelectMany( r => r._allRefs ) );
//                if( refs.Count > 0 ) Depth = refs.Select( r => r.Depth ).Max();
//            }

//            public Referencer( IDotNetDependentProject project, CKTrait f )
//            {
//                Debug.Assert( project != null
//                                && f != null
//                                && f.Context == MSBuildContext.Traits
//                                && f.IsAtomic );
//                Project = project;
//                Framework = f;
//                _allRefs = new HashSet<IProjectFramework>();
//            }

//            public override string ToString() => $"{Project.FullName}({Framework})";
//        }

//        public IEnumerable<Referencer> Create( IEnumerable<ProjectDependencyResult.ProjectDepencyRow> rows )
//        {
//            return rows.SelectMany( r => r.RawPackageDependency.Frameworks.AtomicTraits.Select( f => Create( r.SourceProject, f ) ) );
//        }

//        Referencer Create( IDotNetDependentProject p, CKTrait atomic )
//        {
//            var key = (p, atomic);
//            if( !_referencers.TryGetValue( key, out var referencer ) )
//            {
//                referencer = new Referencer( p, atomic );
//                _referencers.Add( key, referencer );
//                var sources = _dependencies.Where( r => r.TargetPackage.Project == p );
//                referencer.SetRefs( Create( sources ).ToList() );
//            }
//            return referencer;
//        }
//    }

//}
