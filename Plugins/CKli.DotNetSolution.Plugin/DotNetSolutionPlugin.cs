using Buildalyzer;
using CK.Core;
using CKli.BasicDotNetSolution.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace CKli.DotNetSolution.Plugin;

public sealed class DotNetSolutionPlugin : RepoPlugin<DotNetSolutionInfo>
{
    readonly BasicDotNetSolutionPlugin _rawSolutionProvider;

    public DotNetSolutionPlugin( BasicDotNetSolutionPlugin rawSolutionProvider )
        : base( rawSolutionProvider.World )
    {
        _rawSolutionProvider = rawSolutionProvider;
    }

    public static void Register( IPluginCollector services )
    {
        services.AddPrimaryPlugin<DotNetSolutionPlugin>();
    }

    protected override DotNetSolutionInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var rawSolution = _rawSolutionProvider.Get( monitor, repo );
        Throw.DebugAssert( rawSolution.ErrorState is RepoInfoErrorState.None );
        if( rawSolution.Issue != BasicSolutionIssue.None )
        {
            return new DotNetSolutionInfo( rawSolution );
        }
        var analyzerManager = new AnalyzerManager();
        foreach( var path in rawSolution.ProjectFiles.Values )
        {
            analyzerManager.GetProject( path );
        }
        var projects = new Dictionary<NormalizedPath, Project>();

        static Project EnsureProject( Dictionary<NormalizedPath, Project> projects,
                                      BasicSolutionInfo rawSolution,
                                      IAnalyzerResult projectResult )
        {
            Throw.DebugAssert( projectResult.ProjectFilePath.StartsWith( rawSolution.Repo.WorkingFolder + '/' ) );
            var isPackable = projectResult.GetProperty( "IsPackable" ) == "true";
            NormalizedPath projectFileSubPath = projectResult.ProjectFilePath.Substring( rawSolution.Repo.WorkingFolder.Path.Length + 1 );
            if( !projects.TryGetValue( projectFileSubPath, out var project ) )
            {
                project = new Project( projectFileSubPath, isPackable );
                projects.Add( projectFileSubPath, project );
            }
            else
            {
                project._isPackable |= isPackable;
            }
            return project;
        }

        StringBuilder? analyzisError = null;
        var bRefs = ImmutableArray.CreateBuilder<ProjectPackageReference>();

        foreach( var p in analyzerManager.Projects.Values )
        {
            try
            {
                var results = p.Build();
                foreach( var result in results )
                {
                    if( result.Succeeded )
                    {
                        var project = EnsureProject( projects, rawSolution, result );
                        ProcessPackageReferences( bRefs, project, result.TargetFramework, result.PackageReferences );
                    }
                    else
                    {
                        analyzisError ??= new StringBuilder();
                        analyzisError.Append( $"Error while analyzing project '{p.ProjectFile.Name}' in '{repo.DisplayPath}' for target framework '{result.TargetFramework}'." );
                    }
                }
            }
            catch( Exception ex )
            {
                return new DotNetSolutionInfo( rawSolution, ex );
            }
        }
        return analyzisError != null
            ? new DotNetSolutionInfo( rawSolution, analyzisError.ToString() )
            : projects.Count == 0
                ? new DotNetSolutionInfo( rawSolution, "No project found while analyzing solution." )
                : new DotNetSolutionInfo( rawSolution, projects, bRefs.DrainToImmutable() );

        static void ProcessPackageReferences( ImmutableArray<ProjectPackageReference>.Builder bRefs,
                                              Project project,
                                              string targetFramework,
                                              IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> packageReferences )
        {
            foreach( var (name, props) in packageReferences )
            {
                var v = SVersion.Parse( props["Version"] );
                bRefs.Add( new ProjectPackageReference( project, targetFramework, name, v ) );
            }
        }

    }

}
