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
        // The _nodeSolutionHasError flag specifies whether the NodeSolution should
        // actually NOT be null.
        NodeSolution? _nodeSolution;
        bool _noNodeSolution;

        public NodeSolutionProvider( SolutionDriver driver )
        {
            _driver = driver;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// When there is no NodeSolution, then we are never dirty.
        /// </remarks>
        public bool IsDirty => !_noNodeSolution && _nodeSolution == null;

        /// <summary>
        /// Gets the node solution if available.
        /// </summary>
        public NodeSolution? NodeSolution => _nodeSolution;

        /// <inheritdoc/>
        public void SetDirty( IActivityMonitor monitor )
        {
            if( IsDirty ) return;
            monitor.Info( $"Node Solution '{_driver.GitFolder.SubPath}' must be reloaded." );
            _noNodeSolution = false;
            if( _nodeSolution != null )
            {
                _nodeSolution.Saved -= OnSavedSolution;
                _nodeSolution = null;
            }
        }

        void OnSavedSolution( object? sender, EventMonitoredArgs e ) => SetDirty( e.Monitor );

        /// <inheritdoc/>
        public void OnSolutionConfiguration( object? sender, SolutionConfigurationEventArgs e )
        {
            if( !IsDirty ) return;
            var monitor = e.Monitor;
            var repositoryInfoPath = _driver.BranchPath.AppendPart( "RepositoryInfo.xml" );
            _noNodeSolution = true;
            using( monitor.OnError( () => _noNodeSolution = false ) )
            {
                _nodeSolution = NodeSolution.Read( monitor, _driver.GitFolder.FileSystem, repositoryInfoPath );
            }
            if( !_noNodeSolution )
            {
                e.PreventSolutionUse( $"Error while loading Node Solution from '{repositoryInfoPath}'." );
            }
            if( _nodeSolution != null )
            {
                Debug.Assert( !IsDirty );
                Debug.Assert( _nodeSolution.RootProjects.Count > 0 );
                if( !UpdateSolutionFromNode( monitor, e.Solution, _nodeSolution, out bool atLeastOnePublished ) )
                {
                    _noNodeSolution = false;
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
