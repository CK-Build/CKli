using CK.Core;
using CK.Env.DependencyModel;
using System.Diagnostics;
using CK.Env.MSBuildSln;
using System.Collections.Generic;
using System.Linq;
using CK.Env.CKSetup;
using CK.Build;
using System.Xml.Linq;

namespace CK.Env.Plugin
{

    public sealed partial class CKSetupSolutionDriver
    {
        sealed class SolutionProvider : ISolutionProvider
        {
            readonly RepositoryXmlFile _repositoryInfo;

            public SolutionProvider( RepositoryXmlFile repositoryInfo )
            {
                _repositoryInfo = repositoryInfo;
                IsDirty = true;
            }

            public bool IsDirty { get; private set; }

            public void SetDirty( IActivityMonitor monitor ) => IsDirty = true;

            public void ConfigureSolution( object? sender, SolutionConfigurationEventArgs e )
            {
                // Even if we are not dirty, since we depend on the MSBuildSolution,
                // we must handle a change in it.
                var existing = new HashSet<GeneratedArtifact>( e.Solution.GeneratedArtifacts.Where( g => g.Artifact.Type == CKSetupClient.CKSetupType ) );
                var ckSetup = _repositoryInfo.Document?.Root?.Element( "CKSetup" );
                var s = e.Solution.Tag<SolutionFile>();
                // If the MSBuildSolution is not available, we clear the UseCKSetup tag and any GeneratedArtifacts
                // that may exist.
                if( ckSetup == null || s == null )
                {
                    e.Solution.RemoveTags<CKSetupSolutionExtensions.Marker>();
                }
                else
                {
                    e.Solution.Tag( CKSetupSolutionExtensions._marker );
                    if( !SynchronizeGeneratedArtifacts( e.Monitor, e.Solution, existing, ckSetup ) )
                    {
                        e.PreventSolutionUse( $"Error in {_repositoryInfo} CKSetup Component." );
                    }
                }
                if( existing.Count > 0 )
                {
                    e.Monitor.Info( $"Removing {existing.Select( g => g.ToString() )} artifacts from {e.Solution}." );
                    foreach( var g in existing ) g.Project.RemoveGeneratedArtifact( g.Artifact );
                }
                IsDirty = false;
            }

            bool SynchronizeGeneratedArtifacts( IActivityMonitor monitor, Solution solution, HashSet<GeneratedArtifact> existing, XElement ckSetup )
            {
                bool success = true;
                foreach( var path in ckSetup.Elements( "Component" ).Select( e => e.Value ) )
                {
                    if( !string.IsNullOrWhiteSpace( path ) )
                    {
                        var project = solution.Projects.Where( p => p.Tag<MSProject>() != null && p.SolutionRelativeFolderPath == path ).FirstOrDefault();
                        if( project == null )
                        {
                            monitor.Error( $"{_repositoryInfo} CKSetup Component: project path '{path}' not found." );
                            success = false;
                        }
                        else 
                        {
                            if( !project.IsPublished )
                            {
                                monitor.Error( $"Project {project} must be <IsPackable>true</IsPackable> to be a CKSetupComponent." );
                                success = false;
                            }
                            else
                            {
                                var p = project.Tag<MSProject>();
                                Debug.Assert( p != null );
                                foreach( var a in p.TargetFrameworks.AtomicTraits
                                                   .Select( t => new Artifact( NuGet.NuGetClient.NuGetType, path + '/' + t.ToString() ) ) )
                                {
                                    project.AddGeneratedArtifacts( a );
                                    existing.RemoveWhere( g => g.Artifact == a );
                                }
                            }
                        }
                    }
                }
                return success;
            }
        }
    }
}
