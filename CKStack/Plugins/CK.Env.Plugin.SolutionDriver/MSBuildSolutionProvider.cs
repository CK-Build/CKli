using CK.Build;
using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.MSBuildSln;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;

namespace CK.Env.Plugin
{
    sealed class MSBuildSolutionProvider : ISolutionProvider
    {
        readonly SolutionDriver _driver;

        SolutionFile? _sln;

        public MSBuildSolutionProvider( SolutionDriver driver )
        {
            _driver = driver;
        }

        /// <inheritdoc/>
        public bool IsDirty => _sln == null;

        /// <inheritdoc/>
        public void SetDirty( IActivityMonitor monitor )
        {
            if( _sln == null ) return;
            monitor.Info( $"MSBuild Solution '{_driver.GitFolder.SubPath}' must be reloaded." );
            _sln.Saved -= OnSavedSolution;
            _sln = null;
        }

        void OnSavedSolution( object? sender, EventMonitoredArgs e ) => SetDirty( e.Monitor );

        /// <inheritdoc/>
        public void ConfigureSolution( object? sender, SolutionConfigurationEventArgs e )
        {
            if( !IsDirty ) return;
            var monitor = e.Monitor;
            var expectedSolutionName = _driver.GitFolder.SubPath.LastPart + ".sln";
            _sln = SolutionFile.Read( _driver.GitFolder.FileSystem, monitor, _driver.BranchPath.AppendPart( expectedSolutionName ) );
            if( _sln == null )
            {
                e.PreventSolutionUse( $"Unable to load MSBuild Solution '{_driver.GitFolder.SubPath.Path + ".sln"}'." );
                // On error, we remove all the MSBuild projects.
                if( e.Solution.RemoveTags<SolutionFile>() )
                {
                    foreach( var project in e.Solution.Projects.Where( p => p.RemoveTags<MSProject>() ).ToArray() )
                    {
                        e.Solution.RemoveProject( project );
                    }
                }
                return;
            }
            _sln.Saved += OnSavedSolution;
            UpdateSolutionFromMSBuild( monitor, e.Solution, _sln );
        }

        /// <summary>
        /// Updates the <paramref name="solution"/> from a <see cref="SolutionFile"/>.
        /// </summary>
        static void UpdateSolutionFromMSBuild( IActivityMonitor monitor, Solution solution, SolutionFile sln )
        {
            solution.Tag( sln );
            var projectsToRemove = new HashSet<DependencyModel.Project>( solution.Projects.Where( p => p.Type == ".Net" ) );
            var orderedProjects = new DependencyModel.Project[sln.MSProjects.Count];
            int i = 0;
            foreach( var p in sln.MSProjects )
            {
                if( p.ProjectName != p.SolutionRelativeFolderPath.LastPart )
                {
                    monitor.Warn( $"Project named {p.ProjectName} should be in folder of the same name, not in {p.SolutionRelativeFolderPath.LastPart}." );
                }
                Debug.Assert( p.ProjectFile != null );

                var (project, isNewProject) = solution.AddOrFindProject( p.SolutionRelativeFolderPath, ".Net", p.ProjectName );
                ConfigureProject( p, project );
                SynchronizePackageReferences( monitor, project );
                projectsToRemove.Remove( project );
                orderedProjects[i++] = project;
            }

            SynchronizeSolutionPackageReferences( sln, solution );

            foreach( var project in solution.Projects.Where( p => p.Tag<MSProject>() != null ) )
            {
                SynchronizeProjectReferences( monitor, project, msProj => orderedProjects[msProj.MSProjIndex] );
            }
            foreach( var noMore in projectsToRemove ) solution.RemoveProject( noMore );
            return;

            static void ConfigureProject( MSProject p, DependencyModel.Project project )
            {
                project.Tag( p );
                project.Savors = p.TargetFrameworks;
                if( project.SimpleProjectName == "CodeCakeBuilder" )
                {
                    project.IsBuildProject = true;
                }
                else
                {
                    project.IsTestProject = project.SimpleProjectName.EndsWith( ".Tests" );
                    var g = new Artifact( NuGet.NuGetClient.NuGetType, project.SimpleProjectName );
                    if( p.IsPackable ?? false )
                    {
                        project.AddGeneratedArtifacts( g );
                    }
                    else
                    {
                        project.RemoveGeneratedArtifact( g );
                    }
                }
            }

            static void SynchronizeSolutionPackageReferences( SolutionFile sln, Solution solutionModel )
            {
                HashSet<Artifact> solutionRefs = new HashSet<Artifact>( solutionModel.SolutionPackageReferences.Select( p => p.Target.Artifact ) );
                foreach( var p in sln.StandardDotnetToolConfigFile.Tools )
                {
                    solutionRefs.Remove( p.Artifact );
                    solutionModel.AddSolutionPackageReference( p );
                }
                foreach( var p in solutionRefs ) solutionModel.RemoveSolutionPackageReference( p );
            }

            static void SynchronizeProjectReferences( IActivityMonitor m, DependencyModel.Project project, Func<MSProject, DependencyModel.Project> depsFinder )
            {
                var p = project.Tag<MSProject>();
                var toRemove = new HashSet<IProject>( project.ProjectReferences.Select( r => r.Target ) );
                foreach( var dep in p.Deps.Projects )
                {
                    if( dep.TargetProject is MSProject target )
                    {
                        var mapped = depsFinder( target );
                        Debug.Assert( mapped != null );
                        project.EnsureProjectReference( mapped, ArtifactDependencyKind.Transitive );
                        toRemove.Remove( mapped );
                    }
                    else
                    {
                        m.Warn( $"Project '{p}' references project {dep.TargetProject} of unhandled type. Reference is ignored." );
                    }
                }
                foreach( var noMore in toRemove ) project.RemoveProjectReference( noMore );
            }


            static void SynchronizePackageReferences( IActivityMonitor m, DependencyModel.Project project )
            {
                var toRemove = new HashSet<Artifact>( project.PackageReferences.Select( r => r.Target.Artifact ) );
                var p = project.Tag<MSProject>();
                foreach( DeclaredPackageDependency dep in p.Deps.Packages )
                {
                    var d = dep.BaseArtifactInstance;
                    toRemove.Remove( d.Artifact );
                    project.EnsurePackageReference( d,
                                                    dep.PrivateAsset.Equals( "all", StringComparison.OrdinalIgnoreCase ) ? ArtifactDependencyKind.Private : ArtifactDependencyKind.Transitive,
                                                    dep.Frameworks );
                }
                foreach( var noMore in toRemove ) project.RemovePackageReference( noMore );
            }

        }

    }
}
