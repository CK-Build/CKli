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
            monitor.Info( $"Node Solution '{_driver.GitFolder.SubPath}' must be reloaded." );
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
                Debug.Assert( !IsDirty );
                Debug.Assert( _nodeSolution.RootProjects.Count > 0 );
                if( !UpdateSolutionFromNode( monitor, e.Solution, _nodeSolution, out bool atLeastOnePublished ) )
                {
                    _hasNodeSolution = true;
                    e.PreventSolutionUse( $"Error while synchronizing Node Solution from '{repositoryInfoPath}'." );
                }
            }
            // If there is no NodeSolution, whether its because there is no NodeProjects or because
            // we couldn't read it, we remove the Node projects.
            if( _nodeSolution == null )
            {
                if( e.Solution.RemoveTags<NodeSolution>() )
                {
                    foreach( var project in e.Solution.Projects.Where( p => p.RemoveTags<NodeProjectBase>() ).ToArray() )
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
            var toRemove = new HashSet<DependencyModel.Project>( solution.Projects.Where( p => p.Tag<NodeProjectBase>() != null ) );
            foreach( var p in nodeSolution.AllProjects )
            {
                var (project, isNew) = solution.AddOrFindProject( p.SolutionRelativePath, "js", p.PackageJsonFile.SafeName );
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
            foreach( var project in solution.Projects )
            {
                success &= SynchronizeProjectReferences( monitor, project );
            }

            return success;

            static void SynchronizePackageReferences( IActivityMonitor monitor, DependencyModel.Project project, NodeProjectBase p )
            {
                var toRemove = new HashSet<Artifact>( project.PackageReferences.Select( r => r.Target.Artifact ) );
                foreach( var dep in p.PackageJsonFile.Dependencies )
                {
                    if( dep.Version == SVersionBound.None )
                    {
                        monitor.Warn( $"Unable to handle NPM {dep.Kind.ToPackageJsonKey()} '{dep.RawDep}' in {p.PackageJsonFile.FilePath}. Only simple minimal version, or 'file:' relative paths, or 'file' absolute path pointing to a tarball are handled." );
                    }
                    else
                    {
                        var instance = new Artifact( NPMClient.NPMType, dep.Name ).WithVersion( dep.Version.Base );
                        toRemove.Remove( instance.Artifact );
                        project.EnsurePackageReference( instance, dep.Kind );
                    }
                }
                foreach( var noMore in toRemove ) project.RemovePackageReference( noMore );
            }

            static bool SynchronizeProjectReferences( IActivityMonitor monitor, DependencyModel.Project project )
            {
                var p = project.Tag<NodeProjectBase>();
                Debug.Assert( p != null );
                var toRemove = new HashSet<IProject>( project.ProjectReferences.Select( r => r.Target ) );
                foreach( var dep in p.PackageJsonFile.Dependencies )
                {
                    Debug.Assert( project.Solution != null, "Project is not detached from its solution." );
                    if( dep.Type == NodeProjectDependencyType.LocalPath )
                    {
                        var path = project.SolutionRelativeFolderPath.Combine( dep.RawDep.Substring( "file:".Length ) );
                        var mapped = project.Solution.Projects.FirstOrDefault( d => d.SolutionRelativeFolderPath == path.ResolveDots() && d.Type == "js" );
                        if( mapped == null )
                        {
                            monitor.Error( $"Unable to resolve local reference to project '{dep.RawDep}' in {p.PackageJsonFile.FilePath}." );
                            return false;
                        }
                        project.EnsureProjectReference( mapped, dep.Kind );
                        toRemove.Remove( mapped );
                    }
                }
                foreach( var noMore in toRemove ) project.RemoveProjectReference( noMore );
                return true;
            }

        }
    }
}
