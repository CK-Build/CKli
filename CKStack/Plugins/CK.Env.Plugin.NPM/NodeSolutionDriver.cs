using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NPM;

using System;
using System.Diagnostics;
using CK.Env.NodeSln;
using CK.Env.MSBuildSln;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env.Plugin
{
    public sealed class NodeSolutionDriver : GitBranchPluginBase, IDisposable
    {
        readonly NodeSolutionProvider _nodeSolutionProvider;
        readonly SolutionDriver _driver;

        public NodeSolutionDriver( GitRepository f, NormalizedPath branchPath, SolutionDriver driver )
            : base( f, branchPath )
        {
            _driver = driver;
            _nodeSolutionProvider = new NodeSolutionProvider( driver );
            _driver.RegisterSolutionProvider( _nodeSolutionProvider );
            _driver.OnUpdatePackageDependency += OnUpdatePackageDependency;
        }

        /// <summary>
        /// Gets the whole solution driver.
        /// </summary>
        public SolutionDriver SolutionDriver => _driver;

        /// <summary>
        /// Gets whether there is a NodeSolution in this repository even if it cannot be successfully loaded.
        /// This is null when the NodeSolution must be reloaded from the file system.
        /// </summary>
        public bool? HasNodeSolution => _nodeSolutionProvider.HasNodeSolution;

        /// <summary>
        /// Updates a non null <see cref="HasNodeSolution"/> by reloading the solution if it is dirty.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="hasNodeSolution">Whether a Node solution exists in the repository (even if it cannot be successfully loaded).</param>
        /// <returns>True on success, false on error.</returns>
        public bool TryGetHasNodeSolution( IActivityMonitor monitor, out bool hasNodeSolution )
        {
            hasNodeSolution = false;
            if( _nodeSolutionProvider.IsDirty )
            {
                var solution = _driver.GetSolution( monitor, allowInvalidSolution: false );
                if( solution == null ) return false;
            }
            Debug.Assert( !_nodeSolutionProvider.IsDirty && _nodeSolutionProvider.HasNodeSolution.HasValue );
            hasNodeSolution = _nodeSolutionProvider.HasNodeSolution.Value;
            return true;
        }

        /// <summary>
        /// Gets the <see cref="ISolution"/> and <see cref="NodeSolution"/> if the solution
        /// is valid and if a NodeSolution exists.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="solution">The logical solution.</param>
        /// <param name="nodeSolution">The Node solution.</param>
        /// <returns>True on success, false on error.</returns>
        public bool TryGetSolution( IActivityMonitor monitor, [NotNullWhen( true )] out ISolution? solution, [NotNullWhen( true )] out NodeSolution? nodeSolution )
        {
            nodeSolution = null;
            solution = _driver.GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;
            nodeSolution = solution.Tag<NodeSolution>();
            if( nodeSolution == null ) return false;
            return true;
        }

        /// <summary>
        /// Forces this the Node provider to resynchronize the solution.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        public void SetDirty( IActivityMonitor monitor ) => _nodeSolutionProvider.SetDirty( monitor );

        void OnUpdatePackageDependency( object? sender, UpdatePackageDependencyEventArgs e )
        {
            bool mustSave = false;
            foreach( var update in e.UpdateInfo )
            {
                if( update.Referer is IProject project )
                {
                    var p = project.Tag<NodeProjectBase>();
                    if( p != null )
                    {
                        mustSave |= p.PackageJsonFile.SetPackageReferenceVersion( e.Monitor, update.PackageUpdate.Artifact.Name, update.PackageUpdate.Version );
                    }
                }
            }
            if( mustSave )
            {
                Debug.Assert( _nodeSolutionProvider.NodeSolution != null, "Since we found at least one Node project." );
                _nodeSolutionProvider.NodeSolution.Save( e.Monitor );
            }
        }

        void IDisposable.Dispose()
        {
            _driver.OnUpdatePackageDependency -= OnUpdatePackageDependency;
        }

    }
}