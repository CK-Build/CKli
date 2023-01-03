using CK.Core;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.IO;

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
        bool _restoreRequired;

        private protected NodeRootProjectBase( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath, int index )
            : base( solution, path, index )
        {
            _solutionRelativeOutputPath = SolutionRelativePath.Combine( outputPath ).ResolveDots();
            _outputPath = solution.SolutionFolderPath.Combine( _solutionRelativeOutputPath );
        }

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
            if( TryFindYarn( monitor, physicalPath, out var yarnCjs ) )
            {
                return ProcessRunner.Run( monitor,
                                          physicalPath,
                                          "node",
                                          yarnCjs, 10 * 60 * 1000 );
            }
            else
            {
                return ProcessRunner.Run( monitor,
                                          physicalPath,
                                          "cmd.exe",
                                          "/C npm install", 10 * 60 * 1000 );
            }
        }

        bool TryFindYarn( IActivityMonitor monitor, string physicalPath, [NotNullWhen( true )] out string? yarnCjs )
        {
            yarnCjs = null;
            int hopCount = Path.Parts.Count - Solution.SolutionFolderPath.Parts.Count;
            Debug.Assert( hopCount > 0 );
            var searchPath = physicalPath;
            do
            {
                var dir = System.IO.Path.Combine( searchPath, ".yarn" );
                if( Directory.Exists( dir ) )
                {
                    yarnCjs = System.IO.Path.Combine( dir, "releases", "yarn.cjs" );
                    if( !File.Exists( yarnCjs ) )
                    {
                        monitor.Error( $"Missing file 'yarn.cjs' in '{dir}/releases'." );
                        return false;
                    }
                    return true;
                }
            }
            while( --hopCount >= 0 );
            return false;
        }

    }
}


