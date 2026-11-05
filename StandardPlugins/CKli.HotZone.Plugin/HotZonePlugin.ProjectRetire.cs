using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli.HotZone.Plugin;

public sealed partial class HotZonePlugin
{
    /// <summary>
    /// Turns projects into tombstones: the last version of a removed (or renamed) package, without code and without
    /// dependencies, that fails the build of any project that depends on it with an error that names the replacement.
    /// See the HotZone README.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The CKli context.</param>
    /// <param name="projects">The comma-separated project names, optionally prefixed by folders.</param>
    /// <param name="replacement">
    /// The package that replaces the retired ones: its name when the World produces it, otherwise the URL of its README.
    /// </param>
    /// <param name="reason">Why the package is retired, what to do instead.</param>
    /// <param name="dryRun">Only displays what would be done.</param>
    /// <param name="withTests">Also deletes the "X.Tests" project of each retired "X".</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
                  Turns packable projects into tombstones: their code is deleted and their next version is a package
                  without code nor dependencies that fails the build of its consumers with an error naming the
                  replacement. No project of the World must still depend on them.
                  """ )]
    [CommandPath( "project retire" )]
    public bool ProjectRetire( IActivityMonitor monitor,
                               CKliEnv context,
                               [Description( """
                                             Comma-separated project names (the .csproj names), optionally prefixed by folders to
                                             disambiguate ("My-Repo/My.Project").
                                             """ )]
                               string projects,
                               [Description( """
                                             The package that replaces the retired ones: its name when the World produces it (its
                                             documentation URL is derived), otherwise the URL of its README ('.../<PackageId>/README.md').
                                             """ )]
                               [OptionName( "--replacement,-r" )]
                               string? replacement = null,
                               [Description( """
                                             Why the packages are retired and what to do instead. Required without --replacement,
                                             added to the message with it.
                                             """ )]
                               [OptionName( "--reason" )]
                               string? reason = null,
                               [Description( "Displays what would be done (the checks run, the tombstones, the message) and changes nothing." )]
                               [OptionName( "--dry-run,-d" )]
                               bool dryRun = false,
                               [Description( "Also deletes, for each retired project 'X', the project 'X.Tests' of the same repository when it exists." )]
                               bool withTests = false )
    {
        if( string.IsNullOrWhiteSpace( replacement ) && string.IsNullOrWhiteSpace( reason ) )
        {
            monitor.Error( "A tombstone says what to do: --replacement or --reason (or both) must be specified." );
            return false;
        }
        replacement = string.IsNullOrWhiteSpace( replacement ) ? null : replacement.Trim();
        // A URL is the README of a package that the World doesn't produce: the package is named after its folder.
        string? replacementUrl = null;
        if( replacement != null && Uri.TryCreate( replacement, UriKind.Absolute, out var url ) )
        {
            var segments = url.AbsolutePath.Split( '/', StringSplitOptions.RemoveEmptyEntries );
            if( segments.Length < 2 || !segments[^1].Equals( "README.md", StringComparison.OrdinalIgnoreCase ) )
            {
                monitor.Error( $"The --replacement URL '{replacement}' must be the README of the replacement: '.../<PackageId>/README.md'." );
                return false;
            }
            replacementUrl = url.AbsoluteUri;
            replacement = Uri.UnescapeDataString( segments[^2] );
        }
        reason = string.IsNullOrWhiteSpace( reason ) ? null : reason.Trim().TrimEnd( '.' );

        var entries = projects.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
                              .Select( e => new NormalizedPath( e ) )
                              .ToList();
        if( entries.Count == 0 )
        {
            monitor.Error( "No project to retire." );
            return false;
        }
        var allRepos = World.GetAllDefinedRepo( monitor );
        if( allRepos == null ) return false;

        var found = FindProjects( monitor, allRepos, entries );
        if( found == null ) return false;
        var deletedTests = new List<(Repo Repo, NormalizedPath Path)>();
        if( withTests && !AddTestProjects( monitor, found, deletedTests ) ) return false;
        var retiredPaths = found.Except( deletedTests ).ToList();

        // The repositories involved must be clean and on a branch of the branch model: the tombstones land on its
        // "dev/" branch. Their BranchModelInfo is obtained first, while they are clean (see "project move").
        var involved = found.Select( f => f.Repo ).Distinct().OrderBy( r => r.Index ).ToList();
        var branchNamespace = _branchModel.BranchNamespace;
        var hotBranches = new Dictionary<Repo, HotBranch>();
        bool success = true;
        foreach( var r in involved )
        {
            if( !r.GitRepository.CheckCleanCommit( monitor ) )
            {
                success = false;
                continue;
            }
            var info = _branchModel.Get( monitor, r );
            var b = branchNamespace.Find( branchNamespace.RemoveDevPrefix( r.GitRepository.CurrentBranchName, out _ ) );
            if( b == null )
            {
                monitor.Error( $"Repository '{r.DisplayPath}' is on branch '{r.GitRepository.CurrentBranchName}' that is not a branch of the branch model." );
                success = false;
            }
            else
            {
                hotBranches.Add( r, info.Branches[b.Index] );
            }
        }
        if( !success ) return false;

        // Loads the projects of the whole World: the consumers of the retired packages can be anywhere.
        var repoProjects = new Dictionary<Repo, List<MutableProject>>();
        foreach( var r in allRepos )
        {
            var paths = MutableSolution.ReadProjectPaths( monitor, r );
            if( paths == null ) return false;
            var list = new List<MutableProject>();
            foreach( var p in paths )
            {
                if( !p.LastPart.EndsWith( ".csproj", StringComparison.OrdinalIgnoreCase )
                    || !File.Exists( r.WorkingFolder.Combine( p ) ) )
                {
                    continue;
                }
                var mp = MutableProject.Load( monitor, r.WorkingFolder, p );
                if( mp == null ) return false;
                list.Add( mp );
            }
            repoProjects.Add( r, list );
        }
        MutableProject GetProject( Repo r, NormalizedPath path ) => repoProjects[r].First( p => p.Path.Path.Equals( path.Path, StringComparison.OrdinalIgnoreCase ) );

        var retired = retiredPaths.Select( f => (f.Repo, Project: GetProject( f.Repo, f.Path )) ).ToList();
        var deleted = deletedTests.Select( f => (f.Repo, Project: GetProject( f.Repo, f.Path )) ).ToList();
        var gone = retired.Concat( deleted ).Select( x => x.Project ).ToHashSet();
        var retiredIds = new HashSet<string>( retired.Select( x => x.Project.Name ), StringComparer.OrdinalIgnoreCase );

        foreach( var (r, p) in retired )
        {
            if( !IsPackable( p ) )
            {
                monitor.Error( $"Project '{r.DisplayPath}/{p.Path}' produces no package: there is nothing to retire, delete it." );
                success = false;
            }
            else if( IsTombstone( r, p ) )
            {
                monitor.Error( $"Project '{r.DisplayPath}/{p.Path}' is already a tombstone." );
                success = false;
            }
        }
        if( replacement != null && retiredIds.Contains( replacement ) )
        {
            monitor.Error( $"The replacement '{replacement}' is retired." );
            success = false;
        }

        // No project of the World may still depend on a retired package: the tombstone would fail its build.
        var consumers = new List<string>();
        foreach( var (r, list) in repoProjects )
        {
            foreach( var p in list )
            {
                if( gone.Contains( p ) ) continue;
                foreach( var (_, packageId) in p.PackageReferences )
                {
                    if( retiredIds.Contains( packageId ) )
                    {
                        consumers.Add( $"'{r.DisplayPath}/{p.Path}' references the package '{packageId}'." );
                    }
                }
                foreach( var (_, target) in p.ProjectReferences )
                {
                    var q = retired.FirstOrDefault( x => x.Repo == r && x.Project.Path.Path.Equals( target.Path, StringComparison.OrdinalIgnoreCase ) );
                    if( q.Project != null )
                    {
                        consumers.Add( $"'{r.DisplayPath}/{p.Path}' references the project '{q.Project.Name}'." );
                    }
                }
            }
        }
        if( consumers.Count > 0 )
        {
            var hint = !withTests && consumers.Any( c => c.Contains( ".Tests.csproj'", StringComparison.OrdinalIgnoreCase ) )
                        ? $"{Environment.NewLine}The test projects of the retired projects are deleted with --with-tests."
                        : "";
            monitor.Error( $"""
                            The retired packages must not be used any more in the World:
                            {consumers.Concatenate( Environment.NewLine )}
                            Every project must stop depending on them first.{hint}
                            """ );
            success = false;
        }

        // The documentation of the replacement.
        if( replacement != null && replacementUrl == null )
        {
            var producer = repoProjects.SelectMany( kv => kv.Value.Select( p => (Repo: kv.Key, Project: p) ) )
                                       .FirstOrDefault( x => IsPackable( x.Project )
                                                             && x.Project.Name.Equals( replacement, StringComparison.OrdinalIgnoreCase ) );
            if( producer.Project == null )
            {
                monitor.Error( $"No project of the World produces '{replacement}': --replacement must be the URL of its README ('.../{replacement}/README.md')." );
                success = false;
            }
            else
            {
                replacement = producer.Project.Name;
                replacementUrl = GetDocumentationUrl( monitor, producer.Repo, producer.Project.Folder, branchNamespace.Root.Name );
                if( replacementUrl == null ) success = false;
            }
        }
        if( !success ) return false;

        // The plan.
        var plan = new List<(int Indent, string Line)>();
        plan.Add( (0, $"{retired.Count} tombstones:") );
        foreach( var (r, p) in retired )
        {
            plan.Add( (2, $"{r.DisplayPath}/{p.Path}") );
            plan.Add( (4, GetMessage( p.Name, replacement, replacementUrl, reason )) );
        }
        if( deleted.Count > 0 )
        {
            plan.Add( (0, $"Deleted test projects (--with-tests): {deleted.Select( d => $"{d.Repo.DisplayPath}/{d.Project.Path}" ).Concatenate()}.") );
        }
        foreach( var r in involved )
        {
            if( repoProjects[r].Where( IsPackable ).All( p => gone.Contains( p ) ) )
            {
                plan.Add( (0, $"Warning: every package of '{r.DisplayPath}' is retired: once its tombstones are published, the repository has nothing left to publish.") );
            }
        }
        if( dryRun )
        {
            context.Screen.Display( s => s.Text( "Dry run: nothing is changed. The retirement would be:" )! );
            DisplayPlan( context, plan );
            return true;
        }

        using( monitor.OpenInfo( $"Retiring {retired.Count} projects." ) )
        {
            // Lands on the "dev/" branches (the working folders are clean).
            foreach( var (r, hb) in hotBranches )
            {
                var dev = hb.EnsureDevBranch();
                if( !dev.IsCurrentRepositoryHead && !r.GitRepository.Checkout( monitor, dev ) )
                {
                    return false;
                }
            }
            try
            {
                foreach( var (r, p) in retired )
                {
                    var csproj = p.Path;
                    var targetFramework = GetTargetFramework( r, p );
                    DeleteTrackedFiles( r, p.Folder, keep: csproj );
                    var folder = r.WorkingFolder.Combine( p.Folder );
                    var message = GetMessage( p.Name, replacement, replacementUrl, reason );
                    WriteText( r.WorkingFolder.Combine( csproj ), GetTombstoneProject( p.Name, targetFramework, GetDescription( replacement, reason ) ) );
                    WriteText( folder.Combine( $"MSBuild/{p.Name}.targets" ), GetTombstoneTargets( p.Name, message ) );
                    var (readme, package) = GetTombstoneDocumentation( p.Name, replacement, replacementUrl, reason );
                    WriteText( folder.AppendPart( "README.md" ), readme );
                    WriteText( folder.Combine( "docs/Package.md" ), package );
                }
                var solutions = new Dictionary<Repo, MutableSolution>();
                foreach( var (r, p) in deleted )
                {
                    DeleteTrackedFiles( r, p.Folder, keep: default );
                    if( !solutions.TryGetValue( r, out var s ) )
                    {
                        s = MutableSolution.Create( monitor, r );
                        if( s == null ) return false;
                        solutions.Add( r, s );
                    }
                    s.RemoveProject( p.Path );
                }
                foreach( var s in solutions.Values )
                {
                    if( !s.Save( monitor ) ) return false;
                }
            }
            catch( Exception ex )
            {
                monitor.Error( "While writing the tombstones.", ex );
                return false;
            }
        }
        context.Screen.Display( s => s.Text( "Retired:" )! );
        DisplayPlan( context, plan );
        context.Screen.Display( s => s.Text( """
            Nothing is committed: review, list the retired projects as deprecated in the README of their repository (one line),
            build from the World root and commit.
            """ )! );
        return true;
    }

    // A tombstone doesn't include its build output (see GetTombstoneProject).
    static bool IsTombstone( Repo r, MutableProject p )
    {
        var d = XDocument.Load( r.WorkingFolder.Combine( p.Path ) );
        return d.Root!.Elements( "PropertyGroup" )
                      .Elements( "IncludeBuildOutput" )
                      .Any( e => e.Value.Trim().Equals( "false", StringComparison.OrdinalIgnoreCase ) );
    }

    // The target framework of the project file (the first one of its TargetFrameworks), or null when the project
    // inherits it (from a Directory.Build.props).
    static string? GetTargetFramework( Repo r, MutableProject p )
    {
        var d = XDocument.Load( r.WorkingFolder.Combine( p.Path ) );
        var groups = d.Root!.Elements( "PropertyGroup" ).ToList();
        var single = groups.Elements( "TargetFramework" ).Select( e => e.Value.Trim() ).FirstOrDefault( v => v.Length > 0 );
        if( single != null ) return single;
        return groups.Elements( "TargetFrameworks" )
                     .SelectMany( e => e.Value.Split( ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
                     .FirstOrDefault();
    }

    // The README.md of the project's folder on the root branch of its repository: only for the http(s) remotes
    // (GitHub, GitLab and Gitea share the "/blob/<branch>/<path>" layout).
    static string? GetDocumentationUrl( IActivityMonitor monitor, Repo repo, NormalizedPath projectFolder, string rootBranch )
    {
        var origin = repo.OriginUrl;
        if( origin.Scheme != Uri.UriSchemeHttps && origin.Scheme != Uri.UriSchemeHttp )
        {
            monitor.Error( $"The documentation of the replacement can't be derived from the origin '{origin}' of '{repo.DisplayPath}': --replacement must be the URL of its README." );
            return null;
        }
        var root = origin.AbsoluteUri.TrimEnd( '/' );
        if( root.EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) ) root = root[..^4];
        var path = projectFolder.IsEmptyPath ? "README.md" : $"{projectFolder.Path}/README.md";
        return $"{root}/blob/{rootBranch}/{path}";
    }

    static string GetMessage( string packageId, string? replacement, string? replacementUrl, string? reason )
    {
        if( replacement == null )
        {
            return $"Package '{packageId}' is deprecated and has no replacement: {reason}.";
        }
        var why = reason != null ? $" ({reason})" : "";
        return $"Package '{packageId}' is deprecated and must be replaced by '{replacement}'{why}: see {replacementUrl}.";
    }

    static string GetDescription( string? replacement, string? reason )
    {
        return replacement != null ? $"Deprecated: replaced by {replacement}." : $"Deprecated: {reason}.";
    }

    static string GetTombstoneProject( string packageId, string? targetFramework, string description )
    {
        var tf = targetFramework != null ? $"{Environment.NewLine}    <TargetFramework>{targetFramework}</TargetFramework>" : "";
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>{tf}
                <!-- Tombstone: no assembly, no dependencies. -->
                <IncludeBuildOutput>false</IncludeBuildOutput>
                <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
                <!-- NU5128: the package has no lib/ folder, on purpose. -->
                <NoWarn>$(NoWarn);NU5128</NoWarn>
                <Description>{new XText( description )}</Description>
              </PropertyGroup>
              <ItemGroup>
                <Content Include="MSBuild/{packageId}.targets">
                  <Pack>true</Pack>
                  <PackagePath>buildTransitive\{packageId}.targets</PackagePath>
                </Content>
              </ItemGroup>
            </Project>

            """;
    }

    static string GetTombstoneTargets( string packageId, string message )
    {
        // The target name is unique: several tombstones can be imported by the same project.
        var targetName = packageId.Replace( '.', '_' ).Replace( '-', '_' ) + "_IsDeprecated";
        var text = new XAttribute( "Text", message ).ToString();
        return $"""
            <Project>
              <Target Name="{targetName}" BeforeTargets="ResolveAssemblyReferences">
                <Error Code="CKDEPRECATED"
                       {text} />
              </Target>
            </Project>

            """;
    }

    static (string Readme, string Package) GetTombstoneDocumentation( string packageId, string? replacement, string? replacementUrl, string? reason )
    {
        var head = replacement != null
                    ? $"**Deprecated: replaced by `{replacement}`** ([documentation]({replacementUrl}))."
                    : "**Deprecated: no replacement.**";
        var why = reason != null ? $"{Environment.NewLine}{Environment.NewLine}{reason}." : "";
        var readme = $"""
            # {packageId}

            {head}

            This package is a tombstone: it has no code, and a project that depends on it (directly or through another package)
            fails to build with the `CKDEPRECATED` error.{why}

            """;
        var package = $"""
            {head}

            This package is a tombstone: a project that depends on it fails to build (`CKDEPRECATED`).{why}

            """;
        return (readme, package);
    }

    // Deletes the files that git tracks below a folder (except one), then the folders left empty. The ignored files
    // (build outputs) stay.
    static void DeleteTrackedFiles( Repo r, NormalizedPath folder, NormalizedPath keep )
    {
        var prefix = folder.Path + '/';
        var tracked = r.GitRepository.Repository.Index
                         .Select( e => new NormalizedPath( e.Path ) )
                         .Where( p => p.Path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )
                                      && !p.Path.Equals( keep.Path, StringComparison.OrdinalIgnoreCase ) )
                         .ToList();
        foreach( var f in tracked )
        {
            File.Delete( r.WorkingFolder.Combine( f ) );
        }
        var full = r.WorkingFolder.Combine( folder );
        if( Directory.Exists( full ) )
        {
            foreach( var d in Directory.EnumerateDirectories( full, "*", SearchOption.AllDirectories )
                                       .OrderByDescending( d => d.Length )
                                       .ToList() )
            {
                if( !Directory.EnumerateFileSystemEntries( d ).Any() ) Directory.Delete( d );
            }
        }
        // The folder itself and its parents when they are left empty.
        for( var parent = full;
             parent.Parts.Count > r.WorkingFolder.Parts.Count
             && Directory.Exists( parent )
             && !Directory.EnumerateFileSystemEntries( parent ).Any();
             parent = parent.RemoveLastPart() )
        {
            Directory.Delete( parent );
        }
    }

    // UTF-8 without BOM, CRLF.
    static void WriteText( NormalizedPath path, string text )
    {
        Directory.CreateDirectory( path.RemoveLastPart() );
        File.WriteAllText( path, text.ReplaceLineEndings( "\r\n" ), new UTF8Encoding( false ) );
    }
}
