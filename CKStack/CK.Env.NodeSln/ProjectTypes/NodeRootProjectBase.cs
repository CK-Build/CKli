using CK.Core;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

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
        readonly NormalizedPath _yarnPath;
        bool _restoreRequired;

        private protected NodeRootProjectBase( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath )
            : base( solution, path )
        {
            _solutionRelativeOutputPath = SolutionRelativePath.Combine( outputPath ).ResolveDots();
            _outputPath = solution.SolutionFolderPath.Combine( _solutionRelativeOutputPath );
            _yarnPath = GetYarnPath();
        }

        internal NormalizedPath YarnPath => _yarnPath;

        /// <summary>
        /// Gets whether this project uses Yarn instead of NPM: a ".yarn" folder
        /// exists in the project folder or above (up to the repository root).
        /// </summary>
        public bool UseYarn => !_yarnPath.IsEmptyPath;

        /// <summary>
        /// Gets whether a restore of the packages is required
        /// after the file is saved.
        /// <para>
        /// This becomes true when <see cref="PackageJsonFile.SetPackageReferenceVersion(IActivityMonitor, string, CSemVer.SVersion, bool)"/>
        /// with a different version of an existing dependency has been called for this project or any subordinated project.
        /// </para>
        /// </summary>
        public bool RestoreRequired => _restoreRequired;

        /// <summary>
        /// Gets the output path (in the <see cref="FileSystem"/>).
        /// Defaults to <see cref="Path"/>.
        /// </summary>
        public NormalizedPath OutputPath => _outputPath;

        /// <summary>
        /// Gets the output path. Defaults to <see cref="SolutionRelativePath"/>
        /// </summary>
        public NormalizedPath SolutionRelativeOutputPath => _solutionRelativeOutputPath;

        internal override void SetDirty( bool restoreRequired )
        {
            base.SetDirty( restoreRequired );
            _restoreRequired |= restoreRequired;
        }

        /// <summary>
        /// Calls "yarn" or "npm install" to restore the packages if <see cref="RestoreRequired"/> is true.
        /// Does nothing otherwise.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool RestoreDependencies( IActivityMonitor monitor )
        {
            if( _restoreRequired )
            {
                if( !DoRestoreDependencies( monitor ) ) return false;
                _restoreRequired = false;
            }
            return true;
        }

        bool DoRestoreDependencies( IActivityMonitor monitor )
        {
            var physicalPath = Solution.FileSystem.GetFileInfo( Path ).PhysicalPath;
            if( UseYarn )
            {
                return ProcessRunner.Run( monitor,
                                          physicalPath,
                                          "cmd.exe",
                                          "/C yarn", 10 * 60 * 1000 );
            }
            else
            {
                return ProcessRunner.Run( monitor,
                                          physicalPath,
                                          "cmd.exe",
                                          "/C npm install", 10 * 60 * 1000 );
            }
        }

        NormalizedPath GetYarnPath()
        {
            var p = Path;
            int rootDepth = Solution.SolutionFolderPath.Parts.Count;
            do
            {
                var yarn = p.AppendPart( ".yarn" );
                var dirYarn = Solution.FileSystem.GetFileInfo( yarn );
                if( dirYarn.Exists && dirYarn.IsDirectory ) return yarn;
                p = p.RemoveLastPart();
            }
            while( p.Parts.Count >= rootDepth );
            return default;
        }

    }
}


