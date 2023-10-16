using CK.Build;
using CK.Core;
using CK.Env;
using CK.Env.DependencyModel;
using CK.Env.MSBuildSln;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.Intrinsics.Arm;
using ModelProject = CK.Env.DependencyModel.Project;

namespace CKli
{
    public sealed class MSBuildSolutionProvider : ISolutionProvider
    {
        static readonly ArtifactType NuGetType = ArtifactType.Register( "NuGet", true, ';' );

        public bool Configure( IActivityMonitor monitor, GitRepository r, NormalizedPath branchPath, Solution s )
        {
            var expectedSolutionName = r.DisplayPath.LastPart + ".sln";
            var sln = SolutionFile.Read( r.FileSystem, monitor, branchPath.AppendPart( expectedSolutionName ) );
            if( sln == null )
            {
                monitor.Error( $"Unable to load MSBuild Solution '{r.DisplayPath.Path}.sln'." );
                // On error, we remove all the MSBuild projects.
                if( s.RemoveTags<SolutionFile>() )
                {
                    foreach( var project in s.Projects.Where( p => p.RemoveTags<MSProject>() ).ToArray() )
                    {
                        s.RemoveProject( project );
                    }
                }
                return false;
            }
            UpdateSolutionFromMSBuild( monitor, s, sln );
            return true;

            /// <summary>
            /// Updates the <paramref name="solution"/> from a <see cref="SolutionFile"/>.
            /// </summary>
            static void UpdateSolutionFromMSBuild( IActivityMonitor monitor, Solution solution, SolutionFile sln )
            {
                solution.Tag( sln );
                var projectsToRemove = new HashSet<ModelProject>( solution.Projects.Where( p => p.Type == ".Net" ) );
                var orderedProjects = new ModelProject[sln.MSProjects.Count];
                int i = 0;
                foreach( var p in sln.MSProjects )
                {
                    if( p.ProjectName != p.SolutionRelativeFolderPath.LastPart )
                    {
                        monitor.Warn( $"Project named '{p.ProjectName}' should be in folder of the same name, not in '{p.SolutionRelativeFolderPath.LastPart}'." );
                    }
                    Debug.Assert( p.ProjectFile != null );

                    var (project, isNewProject) = solution.AddOrFindProject( p.SolutionRelativeFolderPath, ".Net", p.ProjectName );
                    ConfigureProject( p, project );
                    SynchronizePackageReferences( monitor, project );
                    projectsToRemove.Remove( project );
                    orderedProjects[i++] = project;
                    // We use the Xml annotations to store the MSProject => ModelProject association.
                    // This avoids to implement yet another Tag management. Note that the MSBuildSln model
                    // is unaware of this and this is good: the association is this provider's job.
                    p.ProjectFile.Root.AddAnnotation( project );
                }

                SynchronizeSolutionPackageReferences( sln, solution );

                foreach( var project in solution.Projects.Where( p => p.Tag<MSProject>() != null ) )
                {
                    SynchronizeProjectReferences( monitor, project, msProj => orderedProjects[msProj.MSProjIndex] );
                }
                foreach( var noMore in projectsToRemove ) solution.RemoveProject( noMore );
                return;

                static void ConfigureProject( MSProject p, ModelProject project )
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
                        var g = new Artifact( NuGetType, project.SimpleProjectName );
                        if( p.IsPackable ?? !project.IsTestProject )
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

                static void SynchronizeProjectReferences( IActivityMonitor m, ModelProject project, Func<MSProject, ModelProject> depsFinder )
                {
                    var p = project.Tag<MSProject>();
                    var toRemove = new HashSet<IProject>( project.ProjectReferences.Select( r => r.Target ) );
                    foreach( var dep in p.Deps.Projects )
                    {
                        if( dep.TargetProject is MSProject target )
                        {
                            var mapped = depsFinder( target );
                            Debug.Assert( mapped != null );
                            project.AddProjectReference( mapped, ArtifactDependencyKind.Transitive, dep.Frameworks );
                            toRemove.Remove( mapped );
                        }
                        else
                        {
                            m.Warn( $"Project '{p}' references project {dep.TargetProject} of unhandled type. Reference is ignored." );
                        }
                    }
                    foreach( var noMore in toRemove ) project.RemoveProjectReference( noMore );
                }


                static void SynchronizePackageReferences( IActivityMonitor m, ModelProject project )
                {
                    Debug.Assert( project.Type == ".Net" );
                    var toRemove = new HashSet<Artifact>( project.PackageReferences.Select( r => r.Target.Artifact ) );
                    var p = project.Tag<MSProject>();
                    foreach( DeclaredPackageDependency dep in p.Deps.Packages )
                    {
                        var d = dep.BaseArtifactInstance;
                        toRemove.Remove( d.Artifact );
                        project.AddPackageReference( d,
                                                     dep.PrivateAsset.Equals( "all", StringComparison.OrdinalIgnoreCase ) ? ArtifactDependencyKind.Private : ArtifactDependencyKind.Transitive,
                                                     dep.Frameworks );
                    }
                    foreach( var noMore in toRemove ) project.RemovePackageReference( noMore );
                }
            }
        }

        public bool Localize( IActivityMonitor monitor, SolutionContext solutions )
        {
            var projectInfos = solutions.GetDependencyAnalyser( monitor, false )
                                        .DefaultDependencyContext
                                        .GetProjectsDependencyInfo( NuGetType, solutions );
            foreach( var sModel in solutions )
            {
                var s = sModel.Tag<SolutionFile>();
                if( s != null )
                {
                    s.TransformToLocal( monitor,
                                        a => projectInfos.ArtifactGenerators.GetValueOrDefault( a )?.Tag<MSProject>(),
                                        p =>
                                        {
                                            Debug.Assert( p.ProjectFile != null );
                                            var project = p.ProjectFile.Root.Annotation<ModelProject>();
                                            Debug.Assert( project != null );
                                            var deps = projectInfos.ProjectDependencies.GetValueOrDefault( project, ImmutableHashSet<IProject>.Empty );
                                            return deps.Select( d => d.Tag<MSProject>()! );
                                        } );
                    s.Save( monitor );
                }
            }
            return true;
        }
    }
}
