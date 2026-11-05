using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CKli.HotZone.Plugin;

public sealed partial class HotZonePlugin
{
    /// <summary>
    /// A tombstone project (see "ckli project retire") whose package is published in a stable version: its last
    /// version exists, the project must be removed from the root branch.
    /// </summary>
    /// <param name="ProjectPath">The path of the ".csproj" file, relative to the repository root.</param>
    /// <param name="PackageId">The retired package.</param>
    /// <param name="Version">Its greatest published stable version (the tombstone).</param>
    public sealed record PublishedTombstone( NormalizedPath ProjectPath, string PackageId, SVersion Version );

    /// <summary>
    /// Gets the tombstone projects of a commit whose package is published in a stable version. On the root branch,
    /// they must be removed: they are published once, and a build of the root branch is blocked until they are
    /// removed ("ckli issue --fix").
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="commit">The commit to analyze.</param>
    /// <returns>The published tombstones (often empty) or null on error.</returns>
    public IReadOnlyList<PublishedTombstone>? GetPublishedTombstones( IActivityMonitor monitor, Repo repo, Commit commit )
    {
        var content = _shallowSolution.GetRequiredContent( monitor, repo, commit, useWorkingFolder: false );
        if( content == null ) return null;
        return FindPublishedTombstones( monitor, repo, content.Projects, _shallowSolution.GetFiles( commit, useWorkingFolder: false ) );
    }

    List<PublishedTombstone> FindPublishedTombstones( IActivityMonitor monitor,
                                                      Repo repo,
                                                      IEnumerable<GitSolutionContent.Project> projects,
                                                      INormalizedFileProvider files )
    {
        var result = new List<PublishedTombstone>();
        // The packages of the published stable versions ("local/" and "building/" versions are not published).
        var published = new Dictionary<string, SVersion>( StringComparer.OrdinalIgnoreCase );
        foreach( var (v, _, c) in _versionTag.Get( monitor, repo ).AllVersions )
        {
            if( v.IsPrerelease || v.HasFakeMetadata || v.IsBuildingOrLocal() || c.BuildContentInfo == null ) continue;
            foreach( var id in c.BuildContentInfo.Produced )
            {
                if( !published.TryGetValue( id, out var max ) || max < v ) published[id] = v;
            }
        }
        if( published.Count == 0 ) return result;
        foreach( var p in projects )
        {
            if( !published.TryGetValue( p.Name, out var version ) ) continue;
            var f = files.GetFileInfo( p.Path );
            if( f == null ) continue;
            try
            {
                using var s = f.CreateReadStream();
                if( IsTombstone( XDocument.Load( s ) ) )
                {
                    result.Add( new PublishedTombstone( p.Path, p.Name, version ) );
                }
            }
            catch( Exception ex )
            {
                monitor.Warn( $"Unable to read '{repo.DisplayPath}/{p.Path}'.", ex );
            }
        }
        return result;
    }

    // On the root branch, a tombstone whose stable version is published is removed: its folder is deleted and it
    // is removed from the solution.
    void OnContentIssue( ContentIssueEventArgs e )
    {
        if( !e.Branch.BranchName.IsRoot
            || !e.TryGetSolution( e.Monitor, out var solution )
            || solution == null )
        {
            return;
        }
        var tombstones = FindPublishedTombstones( e.Monitor, e.Repo, solution.Projects, e.Content );
        if( tombstones.Count == 0 ) return;
        var slnxPath = new NormalizedPath( $"{e.Repo.DisplayPath.LastPart}.slnx" );
        var slnx = e.Content.GetFileInfo( slnxPath );
        if( slnx == null ) return;
        foreach( var t in tombstones )
        {
            e.Monitor.Info( ScreenType.ScreenTag,
                            $"Tombstone '{t.PackageId}' of '{e.Repo.DisplayPath}' is published (v{t.Version}): it must be removed from '{e.Branch.BranchName.Name}'." );
            e.Issues.DeleteFolder( t.ProjectPath.RemoveLastPart() );
        }
        string text;
        using( var r = new StreamReader( slnx.CreateReadStream() ) )
        {
            text = r.ReadToEnd();
        }
        e.Issues.UpdateFile( slnxPath, () => RemoveProjects( text, tombstones.Select( t => t.ProjectPath ) ) );
    }

    // Removes projects from a ".slnx" content (and the solution folders left empty).
    static string RemoveProjects( string slnx, IEnumerable<NormalizedPath> projectPaths )
    {
        var doc = XDocument.Parse( slnx );
        var paths = projectPaths.Select( p => p.Path ).ToHashSet( StringComparer.OrdinalIgnoreCase );
        foreach( var p in doc.Root!.Descendants( "Project" )
                                   .Where( p => paths.Contains( new NormalizedPath( (string?)p.Attribute( "Path" ) ).Path ) )
                                   .ToList() )
        {
            p.Remove();
        }
        foreach( var f in doc.Root.Descendants( "Folder" ).Where( f => !f.HasElements ).ToList() )
        {
            f.Remove();
        }
        var b = new StringBuilder();
        using( var w = XmlWriter.Create( b, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, NewLineChars = "\r\n" } ) )
        {
            doc.Root.Save( w );
        }
        return b.Append( "\r\n" ).ToString();
    }
}
