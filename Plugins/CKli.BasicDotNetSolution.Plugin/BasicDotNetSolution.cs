using CK.Core;
using System.Text.RegularExpressions;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CKli.Plugin;

public sealed partial class BasicDotNetSolution : RepoPlugin<BasicSolutionInfo>, IDisposable
{
    public BasicDotNetSolution( World world )
        : base( world )
    {
        world.FixedLayout += World_FixedLayout;
    }

    public static void Register( IPluginCollector collector )
    {
        collector.AddPrimaryPlugin<BasicDotNetSolution>();
    }

    public void Dispose()
    {
        World.FixedLayout -= World_FixedLayout;
    }

    void World_FixedLayout( object? sender, FixedAllLayoutEventArgs e )
    {
        if( TryGetAll( e.Monitor, out var all ) )
        {
            foreach( var info in all )
            {
                if( info.BadFolderProjectNames.Count > 0 )
                {
                }
            }
        }
    }

    protected override BasicSolutionInfo Create( IActivityMonitor monitor, Repo repo )
    {
        // Fast path: the .sln exists.
        var content = ReadSlnFile( repo.DisplayPath.LastPart, out var slnPath );
        if( content != null )
        {
            return LoadProjects( monitor, repo, slnPath, content );
        }
        // No luck.
        int candidateCount = Directory.EnumerateFiles( repo.WorkingFolder, "*.sln*" )
                                      .Count( sln => sln.EndsWith( ".sln", StringComparison.OrdinalIgnoreCase )
                                                     || sln.EndsWith( ".slnx", StringComparison.OrdinalIgnoreCase ) );
        return new BasicSolutionInfo( repo, candidateCount switch
        {
            0 => BasicSolutionIssue.MissingSolution,
            1 => BasicSolutionIssue.BadNameSolution,
            _ => BasicSolutionIssue.MultipleSolution
        }, null, null );

    }

    static BasicSolutionInfo LoadProjects( IActivityMonitor monitor,
                                         Repo repo,
                                         in NormalizedPath slnPath,
                                         string content )
    {
        Dictionary<string, NormalizedPath>? projectsPath = null;
        List<NormalizedPath>? badFolderProjectNames = null;
        List<NormalizedPath>? duplicateProjectNames = null;
        List<NormalizedPath>? missingProjectFiles = null;
        var projects = ExtractProjectPath().Matches( content );
        foreach( Match project in projects )
        {
            var path = repo.WorkingFolder.Combine( project.Value );
            if( !File.Exists( path ) )
            {
                monitor.Warn( $"Project file '{path}' declared in solution file '{slnPath.LastPart}' not found." );
                missingProjectFiles ??= new List<NormalizedPath>();
                missingProjectFiles.Add( path );
            }
            else
            {
                projectsPath ??= new Dictionary<string, NormalizedPath>();
                var name = path.LastPart;
                Throw.DebugAssert( name.EndsWith( ".csproj" ) && ".csproj".Length == 7 );
                name = name.Substring( 0, name.Length - 7 );
                if( !name.Equals( "CodeCakeBuilder", StringComparison.OrdinalIgnoreCase ) )
                {
                    if( !projectsPath.TryAdd( name, path ) )
                    {
                        monitor.Warn( $"Found duplicate project '{name}' in solution file." );
                        duplicateProjectNames ??= new List<NormalizedPath>();
                        duplicateProjectNames.Add( path );
                    }
                    if( path.LastPart != path.Parts[^2] )
                    {
                        badFolderProjectNames ??= new List<NormalizedPath>();
                        badFolderProjectNames.Add( path );
                    }
                }
            }
        }
        if( projectsPath == null )
        {
            return new BasicSolutionInfo( repo, BasicSolutionIssue.MissingSolution, null, null );
        }
        if( duplicateProjectNames != null || missingProjectFiles != null )
        {
            var issue = duplicateProjectNames != null ? BasicSolutionIssue.DuplicateProjects : BasicSolutionIssue.None;
            if( missingProjectFiles != null ) issue |= BasicSolutionIssue.MissingProjects;
            if( issue is BasicSolutionIssue.None ) issue = BasicSolutionIssue.EmptySolution;
            Throw.DebugAssert( issue is not BasicSolutionIssue.None );

            return new BasicSolutionInfo( repo, issue, duplicateProjectNames, missingProjectFiles );
        }
        return new BasicSolutionInfo( repo, projectsPath, slnPath, badFolderProjectNames );
    }

    static string? ReadSlnFile( NormalizedPath solutionFolder, out NormalizedPath slnPath )
    {
        slnPath = solutionFolder.AppendPart( solutionFolder.LastPart + ".sln" );
        if( File.Exists( slnPath ) )
        {
            return File.ReadAllText( slnPath );
        }
        var slnxPath = slnPath.Path + 'x';
        if( File.Exists( slnxPath ) )
        {
            slnPath = slnxPath;
            return File.ReadAllText( slnxPath );
        }
        return null;
    }

    [GeneratedRegex( """(?<=")[^"]*\.csproj(?=")""" )]
    private static partial Regex ExtractProjectPath();
}
