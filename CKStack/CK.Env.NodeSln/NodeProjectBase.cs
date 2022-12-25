using CK.Core;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Base class for all kind of projects.
    /// </summary>
    public abstract class NodeProjectBase
    {
        readonly NormalizedPath _solutionRelativePath;
        readonly NormalizedPath _path;
        [AllowNull]
        PackageJsonFile _packageJson;
        bool _isDirty;
        bool _restoreRequired;

        private protected NodeProjectBase( NodeSolution solution, NormalizedPath path, int index )
        {
            Solution = solution;
            Index = index;
            _solutionRelativePath = path.ResolveDots();
            _path = solution.SolutionFolderPath.Combine( _solutionRelativePath );
        }

        internal virtual bool Initialize( IActivityMonitor monitor )
        {
            var path = Solution.SolutionFolderPath.AppendPart( "package.json" );
            _packageJson = PackageJsonFile.Read( monitor, this );
            return _packageJson != null;
        }


        /// <summary>
        /// Gets the solution that owns this project.
        /// </summary>
        public NodeSolution Solution { get; }

        /// <summary>
        /// Gets the index of this project in the <see cref="NodeSolution.Projects"/> list for root projects.
        /// When this is a <see cref="NodeSubProject"/>, this is the index in the <see cref="INodeWorkspace.Projects"/>
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the path to the project folder relative to the <see cref="NodeSolution.SolutionFolderPath"/>.
        /// <para>
        /// This is always a folder: project file(s) depend on the specific project type.
        /// </para>
        /// </summary>
        public NormalizedPath SolutionRelativePath => _solutionRelativePath;

        /// <summary>
        /// Gets the project path (in the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath Path => _path;

        /// <summary>
        /// Gets the package.json file.
        /// </summary>
        public PackageJsonFile PackageJsonFile => _packageJson;

        /// <summary>
        /// Gets whether this file needs to be saved.
        /// </summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// Gets whether a restore of the packages is required
        /// after the file is saved.
        /// </summary>
        public bool RestoreRequired => _restoreRequired;


        public bool Save( IActivityMonitor monitor )
        {
        }

        protected void SetDirty( bool restoreRequired )
        {
            _isDirty = true;
            _restoreRequired |= restoreRequired;
        }

        public override string ToString() => Path;
    }
}


