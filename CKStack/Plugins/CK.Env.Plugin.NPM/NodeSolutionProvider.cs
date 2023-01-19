using CK.Build;
using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.MSBuildSln;
using CK.Env.NodeSln;
using CK.Env.Plugin;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.NPM
{
    sealed class NodeSolutionProvider : ISolutionProvider
    {
        readonly SolutionDriver _driver;
        // Having a null NodeSolution is perfectly valid: this differs from the MSBuild
        // provider for which a .sln always exists.
        // The _hasNodeSolution flag specifies whether the NodeSolution should
        // actually NOT be null.
        NodeSolution? _nodeSolution;
        bool _isDirty;
        bool _hasNodeSolution;

        public NodeSolutionProvider( SolutionDriver driver )
        {
            _driver = driver;
            _isDirty = true;
        }

        /// <inheritdoc/>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// Gets whether there is a NodeSolution in the repository (even if it cannot be successfully loaded)
        /// or null if <see cref="SetDirty(IActivityMonitor)"/> has been called.
        /// </summary>
        public bool? HasNodeSolution => _isDirty ? null : _hasNodeSolution;

        /// <summary>
        /// Gets whether there is a NodeSolution in the repository (even if it cannot be successfully loaded)
        /// regardless of the <see cref="IsDirty"/> flag.
        /// </summary>
        public bool UnsafeHasNodeSolution => _hasNodeSolution;

        /// <summary>
        /// Gets the node solution if available.
        /// </summary>
        public NodeSolution? NodeSolution => _nodeSolution;

        /// <inheritdoc/>
        public void SetDirty( IActivityMonitor monitor )
        {
            if( _isDirty ) return;
            monitor.Info( $"Node Solution '{_driver.GitFolder.DisplayPath}' must be reloaded." );
            _isDirty = true;
            if( _nodeSolution != null )
            {
                _nodeSolution.Saved -= OnSavedSolution;
                _nodeSolution = null;
            }
        }

        void OnSavedSolution( object? sender, EventMonitoredArgs e ) => SetDirty( e.Monitor );

        /// <inheritdoc/>
        public void ConfigureSolution( object? sender, SolutionConfigurationEventArgs e )
        {
            if( !IsDirty ) return;
            _isDirty = false;
            var monitor = e.Monitor;
            var repositoryInfoPath = _driver.BranchPath.AppendPart( "RepositoryInfo.xml" );
            _hasNodeSolution = false;
            using( monitor.OnError( () => _hasNodeSolution = true ) )
            {
                _nodeSolution = NodeSolution.Read( monitor, _driver.GitFolder.FileSystem, repositoryInfoPath );
            }
            if( _hasNodeSolution )
            {
                e.PreventSolutionUse( $"Error while loading Node Solution from '{repositoryInfoPath}'." );
            }
            if( _nodeSolution != null )
            {
                _hasNodeSolution = true;
                Debug.Assert( !_isDirty );
                Debug.Assert( _nodeSolution.RootProjects.Count > 0 );
                if( !UpdateSolutionFromNode( monitor, e.Solution, _nodeSolution, out bool atLeastOnePublished ) )
                {
                    _nodeSolution = null;
                    e.PreventSolutionUse( $"Error while synchronizing Node Solution from '{repositoryInfoPath}'." );
                }
            }
            // If there is no NodeSolution, whether its because there is no NodeProjects or because
            // we couldn't read it, we remove the Node projects.
            if( _nodeSolution == null )
            {
                if( e.Solution.RemoveTags<NodeSolution>() )
                {
                    foreach( var project in e.Solution.Projects.Where( p => p.Type == "Node" ).ToArray() )
                    {
                        e.Solution.RemoveProject( project );
                    }
                }
            }
        }

        bool UpdateSolutionFromNode( IActivityMonitor monitor, Solution solution, NodeSolution nodeSolution, out bool atLeastOnePublished )
        {
            bool success = true;
            solution.Tag( nodeSolution );

            atLeastOnePublished = false;
            // Ensuring project, their references to packages and their generated artifact (when IsPrivate is false).
            var toRemove = new HashSet<DependencyModel.Project>( solution.Projects.Where( p => p.Type == "Node" ) );
            foreach( var p in nodeSolution.AllProjects )
            {
                var (project, isNew) = solution.AddOrFindProject( p.SolutionRelativePath, "Node", p.PackageJsonFile.SafeName );
                project.Tag( p );
                toRemove.Remove( project );
                // Synchronizing the generated Artifact by first removing any previous.
                if( project.GeneratedArtifacts.Count > 1 )
                {
                    Throw.InvalidOperationException( $"Node project '{p.SolutionRelativePath}' cannot have more than one artifact: '{project.GeneratedArtifacts.Select( g => g.ToString() ).Concatenate()}'." );
                }
                if( project.GeneratedArtifacts.Count == 1 ) project.RemoveGeneratedArtifact( project.GeneratedArtifacts[0].Artifact );
                if( !p.PackageJsonFile.IsPrivate )
                {
                    project.AddGeneratedArtifacts( new Artifact( NPMClient.NPMType, p.PackageJsonFile.Name ) );
                    atLeastOnePublished = true;
                }
                SynchronizePackageReferences( monitor, project, p );
            }
            // Removes the logical projects that don't appear anymore.
            foreach( var project in toRemove ) solution.RemoveProject( project );

            // Now that all the projects have been synchronized, we can handle the project references
            // (the "file://", i.e  NodeProjectDependencyType.LocalPath).
            foreach( var project in solution.Projects.Where( p => p.Type == "Node" ) )
            {
                success &= SynchronizeProjectReferences( monitor, project );
            }

            return success;

            static void SynchronizePackageReferences( IActivityMonitor monitor, DependencyModel.Project project, NodeProjectBase p )
            {
                var toRemove = new HashSet<Artifact>( project.PackageReferences.Select( r => r.Target.Artifact ) );
                foreach( var dep in p.PackageJsonFile.Dependencies )
                {
                    if( dep.Type == NodeProjectDependencyType.LocalFeedTarball
                        || dep.Type == NodeProjectDependencyType.Workspace )
                    {
                        // a Yarn "workspace:*" or "file:...tgz" dependency is a ProjectReference.
                        continue;
                    }
                    if( dep.Version == SVersionBound.None )
                    {
                        monitor.Warn( $"Unable to handle '{dep}' in {p.PackageJsonFile.FilePath}. Only version, 'workspace:*', or 'file:' relative paths, or 'file' absolute path pointing to a tarball are handled." );
                    }
                    else
                    {
                        var instance = new Artifact( NPMClient.NPMType, dep.Name ).WithVersion( dep.Version.Base );
                        toRemove.Remove( instance.Artifact );
                        project.AddPackageReference( instance, dep.Kind );
                    }
                }
                foreach( var noMore in toRemove ) project.RemovePackageReference( noMore );
            }

            static bool SynchronizeProjectReferences( IActivityMonitor monitor, DependencyModel.Project project )
            {
                var p = project.Tag<NodeProjectBase>();
                Debug.Assert( p != null, "Since project.Type == Node." );
                var toRemove = new HashSet<IProject>( project.ProjectReferences.Select( r => r.Target ) );
                foreach( var dep in p.PackageJsonFile.Dependencies )
                {
                    Debug.Assert( project.Solution != null, "Project is not detached from its solution." );

                    var available = project.Solution.Projects.Where( d => d.Type == "Node" );
                    if( dep.Type == NodeProjectDependencyType.LocalFeedTarball
                        || dep.Type == NodeProjectDependencyType.Workspace )
                    {
                        if( !TryAddProjectReference( monitor, project, p, toRemove, dep, available ) )
                        {
                            return false;
                        }
                    }
                }
                foreach( var noMore in toRemove ) project.RemoveProjectReference( noMore );
                return true;

                static bool TryAddProjectReference( IActivityMonitor monitor,
                                                    DependencyModel.Project project,
                                                    NodeProjectBase p,
                                                    HashSet<IProject> toRemove,
                                                    NodeProjectDependency dep,
                                                    IEnumerable<DependencyModel.Project> available )
                {
                    var mapped = available.Where( d => d.SimpleProjectName == dep.Name ).ToArray();
                    if( mapped.Length == 0 )
                    {
                        monitor.Error( $"Unable to resolve project reference '{dep}' in {p.PackageJsonFile.FilePath}. '{dep.Name}' project not found in '{available.Select( p => p.ToString() ).Concatenate( "', '" )}'." );
                        return false;
                    }
                    if( mapped.Length > 1 )
                    {
                        monitor.Error( $"Project reference '{dep}' in {p.PackageJsonFile.FilePath} maps to more than one project: {mapped.Select( p => p.ToString() ).Concatenate( "', '" )}." );
                        return false;
                    }
                    project.AddProjectReference( mapped[0], dep.Kind );
                    toRemove.Remove( mapped[0] );
                    return true;
                }
            }

        }
    }
}
