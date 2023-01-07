using CK.Build;
using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.MSBuildSln;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;
using LibGit2Sharp;
using System.Security.Cryptography;

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
            UpdateSolutionFromMSBuild( monitor, e.Solution, _sln, e.SolutionSpec );
        }

        /// <summary>
        /// Updates the <paramref name="solution"/> from a <see cref="SolutionFile"/> and a <see cref="SolutionSpec"/>.
        /// </summary>
        static void UpdateSolutionFromMSBuild( IActivityMonitor monitor, Solution solution, SolutionFile sln, SolutionSpec solutionSpec )
        {
            solution.Tag( sln );
            var projectsToRemove = new HashSet<DependencyModel.Project>( solution.Projects );
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
                project.Tag( p );
                if( isNewProject )
                {
                    project.TransformSavors( _ => p.TargetFrameworks );
                    ConfigureFromSpec( monitor, project, solutionSpec );
                }
                else
                {
                    var previous = project.Savors;
                    if( previous != p.TargetFrameworks )
                    {
                        var removed = previous.Except( p.TargetFrameworks );
                        monitor.Trace( $"TargetFramework changed from {previous} to {p.TargetFrameworks}." );
                        project.TransformSavors( t =>
                        {
                            var c = t.Except( previous );
                            if( c.AtomicTraits.Count == t.AtomicTraits.Count - previous.AtomicTraits.Count )
                            {
                                // The trait contained all the previous ones: we replace all of them with the new one.
                                return c.Union( p.TargetFrameworks );
                            }
                            // The trait doesn't contain all the previous ones: we must not blindly add the new project's trait,
                            // we only remove the ones that have been removed.
                            return t.Except( removed );
                        } );
                    }
                }
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


            static void ConfigureFromSpec( IActivityMonitor m, DependencyModel.Project project, SolutionSpec spec )
            {
                var msProject = project.Tag<MSProject>();
                Debug.Assert( msProject != null );
                if( project.SimpleProjectName == "CodeCakeBuilder" )
                {
                    project.IsBuildProject = true;
                }
                else
                {
                    project.IsTestProject = project.SimpleProjectName.EndsWith( ".Tests" );
                    // <IsPackable> defaults to false for us!
                    //
                    // Originally (dnx may be), there was no .sln taken into account.
                    // To pack, you did "dotnet pack" in each project you wanted to pack. And basta.
                    //
                    // IsPackable was created to say that when you "dotnet pack" on a .sln, you should NOT pack THE project
                    // (hence a form of "super default" to true).
                    // This allows to understand the terrific NuGet property: WarnOnPackingNonPackableProject
                    //
                    // And then the bacteria invaded the system: for example, the xunit guys who made that IF you depend on xunit,
                    // then IsPackable is false by default...
                    //
                    // One day, we should make a site "https://optout-that-should-have-been-optin-and-vice-versa.horror".
                    //
                    bool isPackable = msProject.IsPackable ?? false;
                    if( msProject.IsPackable ?? false )
                    {
                        project.AddGeneratedArtifacts( new Artifact( NuGet.NuGetClient.NuGetType, project.SimpleProjectName ) );
                    }
                    if( spec.CKSetupComponentProjects.Contains( project.SimpleProjectName ) )
                    {
                        if( !isPackable )
                        {
                            m.Error( $"Project {project} must be <IsPackable>true</IsPackable> to be a CKSetupComponent." );
                        }
                        else
                        {
                            foreach( var name in msProject.TargetFrameworks.AtomicTraits
                                                          .Select( t => new Artifact( NuGet.NuGetClient.NuGetType, project.SimpleProjectName + '/' + t.ToString() ) ) )
                            {
                                project.AddGeneratedArtifacts( name );
                            }
                        }
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
