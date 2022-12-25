using CK.Core;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Base class for projects that are defined by the <see cref="NodeSolution"/>.
    /// They have an optional <see cref="OutputPath"/>.
    /// </summary>
    public abstract class NodeRootProjectBase : NodeProjectBase
    {
        readonly NormalizedPath _solutionRelativeOutputPath;
        readonly NormalizedPath _outputPath;

        private protected NodeRootProjectBase( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath, int index )
            : base( solution, path, index )
        {
            _solutionRelativeOutputPath = SolutionRelativePath.Combine( outputPath ).ResolveDots();
            _outputPath = solution.SolutionFolderPath.Combine( _solutionRelativeOutputPath );
        }

        /// <summary>
        /// Gets the output path (in the <see cref="FileSystem"/>).
        /// Defaults to <see cref="Path"/>.
        /// </summary>
        public NormalizedPath OutputPath => _outputPath;

        /// <summary>
        /// Gets the output path. Defaults to <see cref="SolutionRelativePath"/>
        /// </summary>
        public NormalizedPath SolutionRelativeOutputPath => _solutionRelativeOutputPath;


    }
}


