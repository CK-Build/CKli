using CK.Core;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Base class for all kind of projects, including the <see cref="NodeSubProject"/>.
    /// <see cref="NodeRootProjectBase"/> is the base class for top-level projects.
    /// </summary>
    public abstract class NodeProjectBase
    {
        readonly NormalizedPath _solutionRelativePath;
        readonly NormalizedPath _path;
        [AllowNull]
        PackageJsonFile _packageJson;
        bool _isDirty;

        private protected NodeProjectBase( NodeSolution solution, NormalizedPath path )
        {
            Solution = solution;
            _solutionRelativePath = path.ResolveDots();
            _path = solution.SolutionFolderPath.Combine( _solutionRelativePath );
        }

        internal PackageJsonFile? TryReadPackageJson( IActivityMonitor monitor ) => _packageJson ??= PackageJsonFile.Read( monitor, this )!;

        internal virtual bool Initialize( IActivityMonitor monitor ) => TryReadPackageJson( monitor ) != null;

        /// <summary>
        /// Gets the solution that owns this project.
        /// </summary>
        public NodeSolution Solution { get; }

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
        /// Gets whether this project needs to be saved.
        /// </summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// Saves this project if <see cref="IsDirty"/> is true.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor monitor )
        {
            if( IsDirty )
            {
                if( PackageJsonFile.IsDirty && !PackageJsonFile.Save( monitor ) ) return false;
                if( !DoSave( monitor ) ) return false;
                _isDirty = false;
            }
            return true;
        }

        abstract private protected bool DoSave( IActivityMonitor monitor );

        internal virtual void SetDirty( bool restoreRequired )
        {
            _isDirty = true;
            Solution.SetDirty();
        }

        public sealed override string ToString() => $"{GetType().Name}: {Solution.SolutionFolderPath.LastPart}/{SolutionRelativePath}";
    }
}

