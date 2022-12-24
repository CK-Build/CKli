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
    public partial class SolutionDriver
    {
        /// <summary>
        /// Updates the <paramref name="solution"/> from a <see cref="SolutionFile"/> and a <see cref="SolutionSpec"/>.
        /// </summary>
        static bool UpdateSolutionFromMSBuild( IActivityMonitor m, Solution solution, SolutionFile sln, SolutionSpec solutionSpec )
        {
            var projectsToRemove = new HashSet<DependencyModel.Project>( solution.Projects );
            var orderedProjects = new DependencyModel.Project[sln.MSProjects.Count];
            int i = 0;
            bool badPack = false;
            foreach( var p in sln.MSProjects )
            {
                if( p.ProjectName != p.SolutionRelativeFolderPath.LastPart )
                {
                    m.Warn( $"Project named {p.ProjectName} should be in folder of the same name, not in {p.SolutionRelativeFolderPath.LastPart}." );
                }
                Debug.Assert( p.ProjectFile != null );

                bool doPack = p.IsPackable ?? true;
                if( doPack == true )
                {
                    if( solutionSpec.NotPublishedProjects.Contains( p.Path ) )
                    {
                        m.Error( $"Project {p.ProjectName} that must not be published have the Element IsPackable not set to false." );
                        badPack = true;
                    }
                    else if( !solutionSpec.TestProjectsArePublished && (p.Path.Parts.Contains( "Tests" ) || p.ProjectName.EndsWith( ".Tests" )) )
                    {
                        m.Error( $"Tests Project {p.ProjectName} does not have IsPackable set to false." );
                        badPack = true;
                    }
                    else if( p.ProjectName.Equals( "CodeCakeBuilder", StringComparison.OrdinalIgnoreCase ) )
                    {
                        m.Error( $"CodeCakeBuilder Project does not have IsPackable set to false." );
                        badPack = true;
                    }
                    else
                    {
                        m.Trace( $"Project {p.ProjectName} will be published." );
                    }
                }
                else
                {
                    m.Trace( $"Project {p.ProjectName} is set to not be packaged: this project won't be published." );
                }
                var (project, isNewProject) = solution.AddOrFindProject( p.SolutionRelativeFolderPath, ".Net", p.ProjectName );
                project.Tag( p );
                if( isNewProject )
                {
                    project.TransformSavors( _ => p.TargetFrameworks );
                    ConfigureFromSpec( m, project, solutionSpec );
                }
                else
                {
                    var previous = project.Savors;
                    if( previous != p.TargetFrameworks )
                    {
                        var removed = previous.Except( p.TargetFrameworks );
                        m.Trace( $"TargetFramework changed from {previous} to {p.TargetFrameworks}." );
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
                SynchronizePackageReferences( m, project );
                projectsToRemove.Remove( project );
                orderedProjects[i++] = project;
            }

            SynchronizeSolutionPackageReferences( sln, solution );

            foreach( var project in solution.Projects.Where( p => p.Tag<MSProject>() != null ) )
            {
                SynchronizeProjectReferences( m, project, msProj => orderedProjects[msProj.MSProjIndex] );
            }
            foreach( var noMore in projectsToRemove ) solution.RemoveProject( noMore );
            return !badPack;


            static void ConfigureFromSpec( IActivityMonitor m, DependencyModel.Project project, SolutionSpec spec )
            {
                var msProject = project.Tag<MSProject>();
                if( project.SimpleProjectName == "CodeCakeBuilder" )
                {
                    project.IsBuildProject = true;
                }
                else
                {
                    project.IsTestProject = project.SimpleProjectName.EndsWith( ".Tests" );
                    bool mustPublish;
                    if( spec.PublishedProjects.Count > 0 )
                    {
                        mustPublish = spec.PublishedProjects.Contains( project.SolutionRelativeFolderPath );
                    }
                    else
                    {
                        // We blindly follow the <IsPackable> element... except if it's not defined (ie. it's null) we must "think".
                        if( msProject.IsPackable.HasValue )
                        {
                            mustPublish = msProject.IsPackable.Value;
                        }
                        else
                        {
                            bool notPublishedCheck = !spec.NotPublishedProjects.Contains( project.SolutionRelativeFolderPath );
                            bool notRootDirectory = project.SolutionRelativeFolderPath.Parts.Count == 1;
                            bool ignoreNotRoot = spec.PublishProjectInDirectories && !project.IsTestProject;
                            mustPublish = notPublishedCheck
                                          && (notRootDirectory || ignoreNotRoot || (project.IsTestProject && spec.TestProjectsArePublished));
                        }
                    }
                    if( mustPublish )
                    {
                        project.AddGeneratedArtifacts( new Artifact( NuGetType, project.SimpleProjectName ) );
                    }
                    if( spec.CKSetupComponentProjects.Contains( project.SimpleProjectName ) )
                    {
                        if( !mustPublish )
                        {
                            m.Error( $"Project {project} cannot be a CKSetupComponent since it is not published." );
                        }
                        foreach( var name in msProject.TargetFrameworks.AtomicTraits
                                                      .Select( t => new Artifact( CKSetupType, project.SimpleProjectName + '/' + t.ToString() ) ) )
                        {
                            project.AddGeneratedArtifacts( name );
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
                    solutionModel.EnsureSolutionPackageReference( p );
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
