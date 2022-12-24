using CK.Core;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Base class for <see cref="NPMProject"/>, <see cref="AngularWorkspace"/> and <see cref="YarnWorkspace"/>.
    /// </summary>
    public abstract class NodeProjectBase
    {
        readonly NormalizedPath _solutionRelativePath;
        readonly NormalizedPath _solutionRelativeOutputPath;
        readonly NormalizedPath _path;
        readonly NormalizedPath _outputPath;
        [AllowNull]
        PackageJsonFile _packageJson;
        bool _isDirty;

        private protected NodeProjectBase( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath, int index )
        {
            Solution = solution;
            Index = index;
            _solutionRelativePath = path.ResolveDots();
            _solutionRelativeOutputPath = _solutionRelativePath.Combine( outputPath ).ResolveDots();
            _path = solution.SolutionFolderPath.Combine( _solutionRelativePath );
            _outputPath = solution.SolutionFolderPath.Combine( _solutionRelativeOutputPath );
        }

        /// <summary>
        /// Gets the solution that owns this project.
        /// </summary>
        public NodeSolution Solution { get; }

        /// <summary>
        /// Gets the index of this project in the <see cref="NodeSolution.AllProjects"/> list.
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
        /// Gets the output path (in the <see cref="FileSystem"/>).
        /// Defaults to <see cref="Path"/>.
        /// </summary>
        public NormalizedPath OutputPath => _outputPath;

        /// <summary>
        /// Gets whether this file needs to be saved.
        /// </summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// Gets the output path. Defaults to <see cref="SolutionRelativePath"/>
        /// </summary>
        public NormalizedPath SolutionRelativeOutputPath => _solutionRelativeOutputPath;


        public bool Save( IActivityMonitor monitor )
        {
        }

        internal virtual bool Initialize( IActivityMonitor monitor )
        {
            var path = Solution.SolutionFolderPath.AppendPart( "package.json" );
            _packageJson = PackageJsonFile.Read( monitor, this );
            return _packageJson != null;
        }

        protected void SetDirty() => _isDirty = true;
    }


}


