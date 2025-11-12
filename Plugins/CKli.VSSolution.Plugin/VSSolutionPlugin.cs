using CK.Core;
using CKli.Core;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Microsoft.VisualStudio.SolutionPersistence has only Async API.
// Considering that RepoInfo should be obtained in an async context would
// really complexify the API.
// So we sync over async here :-(.
//
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits

namespace CKli.VSSolution.Plugin;

public sealed partial class VSSolutionPlugin : RepoPlugin<VSSolutionInfo>
{
    public VSSolutionPlugin( PrimaryPluginContext context )
        : base( context.World )
    {
        World.Events.Issue += OnIssue;
    }

    void OnIssue( IssueEvent e )
    {
        foreach( var r in e.Repos )
        {
            var s = Get( e.Monitor, r );
            if( s.Issue != VSSolutionIssue.None )
            {
                e.Add( s.CreateIssue( e.ScreenType ) );
            }
        }
    }

    protected override VSSolutionInfo Create( IActivityMonitor monitor, Repo repo )
    {
        // Fast path: the .sln or .slnx exists.
        var content = ReadSlnOrSlnxFile( repo.WorkingFolder, out var slnPath );
        if( content != null )
        {
            return LoadProjects( monitor, repo, slnPath, content );
        }
        // No luck.
        var candidates = Directory.EnumerateFiles( repo.WorkingFolder, "*.sln*" )
                                  .Where( sln => sln.EndsWith( ".sln", StringComparison.OrdinalIgnoreCase )
                                                 || sln.EndsWith( ".slnx", StringComparison.OrdinalIgnoreCase ) )
                                  .ToList();
        monitor.Warn( $"Unable to read solution from '{repo.DisplayPath}'." );
        return new VSSolutionInfo( repo, candidates );
    }

    static string? GetValidProjectName( NormalizedPath filePath, SolutionProjectModel p, ref List<SolutionProjectModel>? ignoredProjects )
    {
        Throw.DebugAssert( ".csproj".Length == 7 );
        if( !filePath.LastPart.EndsWith( ".csproj" )
            || filePath.LastPart[..^7].Equals( "CodeCakeBuilder", StringComparison.OrdinalIgnoreCase ) )
        {
            ignoredProjects ??= new List<SolutionProjectModel>();
            ignoredProjects.Add( p );
            return null;
        }
        return filePath.LastPart[0..^7];
    }

    static VSSolutionInfo LoadProjects( IActivityMonitor monitor,
                                        Repo repo,
                                        in NormalizedPath slnPath,
                                        SolutionModel solution )
    {
        Dictionary<string, SolutionProjectModel>? projects = null;
        List<SolutionProjectModel>? missingProjectFiles = null;
        List<SolutionProjectModel>? ignoredProjects = null;

        VSSolutionIssue issue = VSSolutionIssue.None;
        foreach( var p in solution.SolutionProjects )
        {
            var path = repo.WorkingFolder.Combine( p.FilePath ).ResolveDots();
            if( !File.Exists( path ) )
            {
                monitor.Warn( $"Project file '{path}' declared in solution file '{slnPath.LastPart}' not found." );
                missingProjectFiles ??= new List<SolutionProjectModel>();
                missingProjectFiles.Add( p );
                issue |= VSSolutionIssue.MissingProjects;
            }
            else
            {
                var name = GetValidProjectName( path, p, ref ignoredProjects );
                if( name != null )
                {
                    projects ??= new Dictionary<string, SolutionProjectModel>( StringComparer.OrdinalIgnoreCase );
                    if( !projects.TryAdd( name, p ) )
                    {
                        monitor.Warn( $"Found duplicate project '{name}' in solution file '{slnPath.LastPart}'." );
                        issue |= VSSolutionIssue.DuplicateProjects;
                    }
                }
            }
        }
        if( projects == null )
        {
            return new VSSolutionInfo( repo, VSSolutionIssue.EmptySolution, solution, ignoredProjects, null );
        }
        if( issue != VSSolutionIssue.None )
        {
            return new VSSolutionInfo( repo, issue, solution, ignoredProjects, missingProjectFiles );
        }
        // Success.
        return new VSSolutionInfo( repo, slnPath, solution, projects, ignoredProjects );
    }

    static SolutionModel? ReadSlnOrSlnxFile( NormalizedPath solutionFolder, out NormalizedPath slnPath )
    {
        slnPath = solutionFolder.AppendPart( solutionFolder.LastPart + ".sln" );
        if( File.Exists( slnPath ) )
        {
            return Load( slnPath );
        }
        var slnxPath = slnPath.Path + 'x';
        if( File.Exists( slnxPath ) )
        {
            slnPath = slnxPath;
            return Load( slnxPath );
        }
        return null;

        static SolutionModel Load( NormalizedPath slnPath )
        {
            var s = SolutionSerializers.GetSerializerByMoniker( slnPath );
            Throw.DebugAssert( s != null );
            var t = s.OpenAsync( slnPath, default );
            t.Wait();
            return t.Result;
        }
    }
}
