using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.HotZone.Plugin;

[CommandNamespace( "project",
    """
    The projects of the repositories: "project move" moves projects to a folder of a repository, from the same
    repository (the references follow) or from other repositories of the World (the references that cross a
    repository boundary become package references).
    """,
    Summary = "Moves projects inside a repository or across repositories.",
    HelpUrl = "https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.HotZone.Plugin/README.md#how-project-move" )]
public sealed partial class HotZonePlugin
{
    sealed class MovedProject
    {
        public required Repo Source { get; init; }
        public required MutableProject Project { get; init; }
        public required NormalizedPath OldPath { get; init; }
        public NormalizedPath NewPath { get; set; }
        public NormalizedPath OldFolder => OldPath.RemoveLastPart();
        public NormalizedPath NewFolder => NewPath.RemoveLastPart();
        public string Name => Project.Name;
        // The files of the folder that git tracks (relative to the source's working folder): only them are moved.
        public IReadOnlyList<NormalizedPath> TrackedFiles { get; set; } = [];
        // The other files of the folder (ignored by git: the repository is clean): they stay where they are.
        public int IgnoredFileCount { get; set; }
    }

    /// <summary>
    /// Moves projects to the current directory (or to <paramref name="target"/>): inside its repository, or from other
    /// repositories of the World. See the HotZone README.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The CKli context.</param>
    /// <param name="projects">The comma-separated project names, optionally prefixed by folders.</param>
    /// <param name="target">The destination folder. Defaults to the current directory.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
                  Moves projects to the current directory (or to --target): inside its repository, or from other
                  repositories of the World. The moved projects keep their layout relative to each other.
                  """ )]
    [CommandPath( "project move" )]
    public bool ProjectMove( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description( """
                                           Comma-separated project names (the .csproj names), optionally prefixed by folders to
                                           disambiguate ("Tests/My.Project.Tests" or "My-Repo/Tests/My.Project.Tests").
                                           """ )]
                             string projects,
                             [Description( "The destination folder, relative to the current directory. Defaults to the current directory." )]
                             [OptionName( "--target,-t" )]
                             string? target = null,
                             [Description( "Displays what the move would do (the moved folders, the reference changes, the starting version) and changes nothing." )]
                             [OptionName( "--dry-run,-d" )]
                             bool dryRun = false,
                             [Description( """
                                           Also moves, for each moved project 'X', the project 'X.Tests' of the same repository when
                                           it exists.
                                           """ )]
                             bool withTests = false )
    {
        // The destination.
        var destination = target == null ? context.CurrentDirectory : context.CurrentDirectory.Combine( target ).ResolveDots();
        var targetRepo = destination.StartsWith( World.Name.WorldRoot, strict: false )
                            ? World.TryGetRepo( monitor, destination )
                            : null;
        if( targetRepo == null )
        {
            monitor.Error( $"The destination '{destination}' is not in a repository of the World." );
            return false;
        }
        var destinationFolder = destination.RemovePrefix( targetRepo.WorkingFolder );

        var entries = projects.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
                              .Select( e => new NormalizedPath( e ) )
                              .ToList();
        if( entries.Count == 0 )
        {
            monitor.Error( "No project to move." );
            return false;
        }
        var allRepos = World.GetAllDefinedRepo( monitor );
        if( allRepos == null ) return false;

        // Finds the projects in the whole World.
        var found = FindProjects( monitor, allRepos, entries );
        if( found == null ) return false;
        var addedTests = new List<(Repo Repo, NormalizedPath Path)>();
        if( withTests && !AddTestProjects( monitor, found, addedTests ) ) return false;

        // The repositories involved: they must be clean and on the same branch of the branch model.
        // Their BranchModelInfo is obtained first, while they are clean: obtaining it deletes a useless "dev/" branch
        // (no commit of its own, typically just after "ckli branch switch --create") and checks out its base. The
        // branches are compared without their "dev/" prefix, and the move lands on the "dev/" branches (see below).
        var involved = found.Select( f => f.Repo ).Append( targetRepo ).Distinct().OrderBy( r => r.Index ).ToList();
        var branchNamespace = _branchModel.BranchNamespace;
        var hotBranches = new Dictionary<Repo, HotBranch>();
        BranchName? branch = null;
        bool success = true;
        foreach( var r in involved )
        {
            if( !r.GitRepository.CheckCleanCommit( monitor ) )
            {
                success = false;
                continue;
            }
            var info = _branchModel.Get( monitor, r );
            var current = branchNamespace.RemoveDevPrefix( r.GitRepository.CurrentBranchName, out _ );
            var b = branchNamespace.Find( current );
            if( b == null )
            {
                monitor.Error( $"Repository '{r.DisplayPath}' is on branch '{r.GitRepository.CurrentBranchName}' that is not a branch of the branch model." );
                success = false;
            }
            else if( branch != null && b != branch )
            {
                monitor.Error( $"Repository '{r.DisplayPath}' is on branch '{b.Name}': all the repositories must be on the same branch ('{branch.Name}')." );
                success = false;
            }
            else
            {
                branch = b;
                hotBranches.Add( r, info.Branches[b.Index] );
            }
        }
        if( !success ) return false;

        // Loads the solutions and the projects of the involved repositories.
        var solutions = new Dictionary<Repo, MutableSolution>();
        var repoProjects = new Dictionary<Repo, List<MutableProject>>();
        foreach( var r in involved )
        {
            var s = MutableSolution.Create( monitor, r );
            if( s == null ) return false;
            solutions.Add( r, s );
            var list = new List<MutableProject>();
            foreach( var p in s.ProjectPaths )
            {
                if( !File.Exists( r.WorkingFolder.Combine( p ) ) ) continue;
                var mp = MutableProject.Load( monitor, r.WorkingFolder, p );
                if( mp == null ) return false;
                list.Add( mp );
            }
            repoProjects.Add( r, list );
        }

        // The moved projects and their new paths: their common folder is replaced by the destination folder.
        var moved = found.Select( f => new MovedProject
                                       {
                                           Source = f.Repo,
                                           OldPath = f.Path,
                                           Project = repoProjects[f.Repo].First( p => p.Path.Path.Equals( f.Path.Path, StringComparison.OrdinalIgnoreCase ) )
                                       } ).ToList();
        var common = moved.Select( m => m.OldFolder.RemoveLastPart() )
                          .Aggregate( ( a, b ) => CommonPrefix( a, b ) );
        foreach( var m in moved )
        {
            m.NewPath = destinationFolder.Combine( m.OldPath.RemoveFirstPart( common.Parts.Count ) );
        }
        if( !CheckDestinations( monitor, targetRepo, moved ) ) return false;
        foreach( var m in moved )
        {
            var prefix = m.OldFolder.Path + '/';
            m.TrackedFiles = m.Source.GitRepository.Repository.Index
                                .Select( e => new NormalizedPath( e.Path ) )
                                .Where( p => p.Path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
                                .ToList();
            var folder = m.Source.WorkingFolder.Combine( m.OldFolder );
            m.IgnoredFileCount = Directory.Exists( folder )
                                    ? Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ).Count() - m.TrackedFiles.Count
                                    : 0;
        }

        var movedByProject = moved.ToDictionary( m => m.Project );
        Repo FinalRepo( Repo r, MutableProject p ) => movedByProject.ContainsKey( p ) ? targetRepo : r;
        NormalizedPath FinalPath( MutableProject p ) => movedByProject.TryGetValue( p, out var m ) ? m.NewPath : p.Path;

        // The packages produced by the involved repositories once the move is done.
        var finalProducers = new Dictionary<string, (Repo Repo, MutableProject Project)>( StringComparer.OrdinalIgnoreCase );
        foreach( var (r, list) in repoProjects )
        {
            foreach( var p in list )
            {
                if( IsPackable( p ) ) finalProducers[p.Name] = (FinalRepo( r, p ), p);
            }
        }

        // The starting version of the target when it receives packable projects from other repositories.
        SVersion? startVersion = null;
        var packableSources = moved.Where( m => m.Source != targetRepo && IsPackable( m.Project ) )
                                   .Select( m => m.Source )
                                   .Distinct()
                                   .ToList();
        if( packableSources.Count > 0 )
        {
            var max = packableSources.Append( targetRepo ).Select( r => GetHighestVersion( monitor, r ) ).Max()!;
            startVersion = SVersion.Create( max.Major, max.Minor, max.Patch + 1 );
        }

        // Plans the reference changes (nothing is changed on error).
        var changes = new List<(string Description, Action Apply)>();
        var dependencies = new HashSet<(Repo From, Repo To)>();
        foreach( var (r, list) in repoProjects )
        {
            foreach( var p in list )
            {
                var finalRepoP = FinalRepo( r, p );
                foreach( var (e, refPath) in p.ProjectReferences )
                {
                    if( refPath.IsEmptyPath ) continue;
                    var q = list.FirstOrDefault( o => o.Path.Path.Equals( refPath.Path, StringComparison.OrdinalIgnoreCase ) );
                    if( q == null ) continue;
                    var finalRepoQ = FinalRepo( r, q );
                    var oldInclude = (string?)e.Attribute( CKli.Core.XNames.Include );
                    if( finalRepoP == finalRepoQ )
                    {
                        // A reference inside a repository: its path follows the moved projects.
                        var qPath = FinalPath( q );
                        var newInclude = MutableProject.GetRelativePath( FinalPath( p ).RemoveLastPart(), qPath );
                        if( newInclude != oldInclude )
                        {
                            changes.Add( ($"{p.Name}: project reference '{oldInclude}' -> '{newInclude}'.",
                                          () => p.SetProjectReference( e, qPath )) );
                        }
                    }
                    else if( !IsPackable( q ) )
                    {
                        monitor.Error( $"""
                                        '{r.DisplayPath}/{p.Path}' references '{q.Name}' that is not packable: after the move, they are in
                                        different repositories and a reference to a non packable project can't become a package reference.
                                        Move them together, or neither of them.
                                        """ );
                        success = false;
                    }
                    else
                    {
                        dependencies.Add( (finalRepoP, finalRepoQ) );
                        var version = GetReferenceVersion( monitor, repoProjects, finalRepoP, q.Name, finalRepoQ, targetRepo, startVersion );
                        var qName = q.Name;
                        changes.Add( ($"{p.Name}: project reference '{oldInclude}' -> package reference '{qName}' {version}.",
                                      () => p.SetPackageReference( e, qName, version )) );
                    }
                }
                foreach( var (e, packageId) in p.PackageReferences )
                {
                    if( !finalProducers.TryGetValue( packageId, out var producer ) ) continue;
                    if( producer.Repo == finalRepoP )
                    {
                        // The package is now produced by the same repository: it becomes a project reference.
                        if( producer.Project != p )
                        {
                            var producerPath = FinalPath( producer.Project );
                            var newInclude = MutableProject.GetRelativePath( FinalPath( p ).RemoveLastPart(), producerPath );
                            changes.Add( ($"{p.Name}: package reference '{packageId}' -> project reference '{newInclude}'.",
                                          () => p.SetProjectReference( e, producerPath )) );
                        }
                    }
                    else
                    {
                        dependencies.Add( (finalRepoP, producer.Repo) );
                    }
                }
            }
        }
        // No cycle between the target and a source.
        foreach( var (from, to) in dependencies )
        {
            if( from == targetRepo && dependencies.Contains( (to, targetRepo) ) )
            {
                monitor.Error( $"""
                                After the move, '{targetRepo.DisplayPath}' and '{to.DisplayPath}' would depend on each other: this is a cycle.
                                Moving the projects in another order (the downstream ones first) or moving more projects may avoid it.
                                """ );
                success = false;
            }
        }
        if( !success ) return false;

        // The plan: displayed by a dry run, and after the move.
        var plan = new List<(int Indent, string Line)>();
        if( addedTests.Count > 0 )
        {
            plan.Add( (0, $"Test projects added by --with-tests: {addedTests.Select( t => t.Path.LastPart[..^".csproj".Length] ).Concatenate()}.") );
        }
        plan.Add( (0, $"{moved.Count} projects to '{targetRepo.DisplayPath}/{destinationFolder}':") );
        foreach( var m in moved )
        {
            var solutionFolder = MutableSolution.GetSolutionFolderPath( m.NewPath );
            plan.Add( (2, $"{m.Source.DisplayPath}/{m.OldFolder} -> {targetRepo.DisplayPath}/{m.NewFolder} "
                          + (solutionFolder.IsEmptyPath ? "(root of the solution)." : $"(solution folder '/{solutionFolder}/').")) );
            if( m.IgnoredFileCount > 0 )
            {
                plan.Add( (4, $"{m.TrackedFiles.Count} tracked files move; {m.IgnoredFileCount} ignored files stay in '{m.Source.DisplayPath}/{m.OldFolder}'.") );
            }
        }
        if( startVersion != null )
        {
            plan.Add( (0, $"Starting version of '{targetRepo.DisplayPath}': 'v{startVersion}+fake'.") );
        }
        if( changes.Count > 0 )
        {
            plan.Add( (0, $"{changes.Count} reference changes:") );
            plan.AddRange( changes.Select( c => (2, c.Description) ) );
        }
        foreach( var m in moved.Where( m => m.OldFolder.Parts.Count != m.NewFolder.Parts.Count ) )
        {
            foreach( var v in m.Project.OtherParentRelativePaths )
            {
                plan.Add( (0, $"Warning: '{m.Name}' changes of depth, its relative path '{v}' is not updated.") );
            }
        }
        if( dryRun )
        {
            context.Screen.Display( s => s.Text( "Dry run: nothing is changed. The move would be:" )! );
            DisplayPlan( context, plan );
            return true;
        }

        using( monitor.OpenInfo( $"Moving {moved.Count} projects to '{targetRepo.DisplayPath}/{destinationFolder}'." ) )
        {
            // The target's version first, while the working folders are clean (this may create an empty commit).
            if( startVersion != null
                && !_versionTag.BumpVersion( monitor, targetRepo, context.Committer, startVersion ) )
            {
                return false;
            }
            // The move lands on the "dev/" branches: the working folders are still clean, the "dev/" branch is
            // created where it is missing (it may have been deleted as useless above) and checked out.
            foreach( var (r, hb) in hotBranches )
            {
                var dev = hb.EnsureDevBranch();
                if( !dev.IsCurrentRepositoryHead && !r.GitRepository.Checkout( monitor, dev ) )
                {
                    return false;
                }
            }
            // Moves the tracked files of the folders: the ignored files (build outputs, the state of a package manager,
            // a local setting...) are generated for their place, or are not ours to move. They stay where they are.
            foreach( var m in moved )
            {
                var from = m.Source.WorkingFolder.Combine( m.OldFolder );
                var to = targetRepo.WorkingFolder.Combine( m.NewFolder );
                try
                {
                    foreach( var f in m.TrackedFiles )
                    {
                        var fileTarget = to.Combine( f.RemoveFirstPart( m.OldFolder.Parts.Count ) );
                        Directory.CreateDirectory( fileTarget.RemoveLastPart() );
                        File.Move( m.Source.WorkingFolder.Combine( f ), fileTarget );
                    }
                    // The folders left empty are removed: below the moved folder, the folder itself and its parents.
                    if( Directory.Exists( from ) )
                    {
                        foreach( var d in Directory.EnumerateDirectories( from, "*", SearchOption.AllDirectories )
                                                   .OrderByDescending( d => d.Length )
                                                   .ToList() )
                        {
                            if( !Directory.EnumerateFileSystemEntries( d ).Any() ) Directory.Delete( d );
                        }
                    }
                    for( var parent = from;
                         parent.Parts.Count > m.Source.WorkingFolder.Parts.Count
                         && Directory.Exists( parent )
                         && !Directory.EnumerateFileSystemEntries( parent ).Any();
                         parent = parent.RemoveLastPart() )
                    {
                        Directory.Delete( parent );
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( $"While moving '{m.Source.DisplayPath}/{m.OldFolder}' to '{targetRepo.DisplayPath}/{m.NewFolder}'.", ex );
                    return false;
                }
                m.Project.SetLocation( targetRepo.WorkingFolder, m.NewPath );
            }
            // Rewrites the references.
            foreach( var c in changes ) c.Apply();
            foreach( var p in repoProjects.Values.SelectMany( l => l ) )
            {
                if( !p.Save( monitor ) ) return false;
            }
            // Updates the solutions.
            foreach( var m in moved )
            {
                solutions[m.Source].RemoveProject( m.OldPath );
                solutions[targetRepo].AddProject( m.NewPath );
            }
            foreach( var s in solutions.Values )
            {
                if( !s.Save( monitor ) ) return false;
            }
        }
        context.Screen.Display( s => s.Text( "Moved:" )! );
        DisplayPlan( context, plan );
        context.Screen.Display( s => s.Text( "Nothing is committed: review, build from the World root and commit." )! );
        return true;
    }

    // One display per line: a multi-line text block trims its lines (the indentation would be lost).
    static void DisplayPlan( CKliEnv context, List<(int Indent, string Line)> plan )
    {
        foreach( var (indent, line) in plan )
        {
            context.Screen.Display( s => s.Text( line )!.Box( marginLeft: 2 + indent ) );
        }
    }

    static bool IsPackable( MutableProject p ) => p.IsPackable != false && !p.Name.EndsWith( ".Tests", StringComparison.OrdinalIgnoreCase );

    static NormalizedPath CommonPrefix( NormalizedPath a, NormalizedPath b )
    {
        int i = 0;
        int max = Math.Min( a.Parts.Count, b.Parts.Count );
        while( i < max && string.Equals( a.Parts[i], b.Parts[i], StringComparison.OrdinalIgnoreCase ) ) ++i;
        return a.RemoveLastPart( a.Parts.Count - i );
    }

    // Finds the project of each entry in the solutions of the World: an entry is matched against the end of
    // "<repository display path>/<project folder's parent>/<project name>", segment by segment.
    List<(Repo Repo, NormalizedPath Path)>? FindProjects( IActivityMonitor monitor, IReadOnlyList<Repo> allRepos, List<NormalizedPath> entries )
    {
        var candidates = new List<(Repo Repo, NormalizedPath Path, NormalizedPath WorldPath)>();
        foreach( var r in allRepos )
        {
            var paths = MutableSolution.ReadProjectPaths( monitor, r );
            if( paths == null ) return null;
            foreach( var p in paths )
            {
                if( !p.LastPart.EndsWith( ".csproj", StringComparison.OrdinalIgnoreCase ) ) continue;
                var folder = p.RemoveLastPart();
                var worldPath = r.DisplayPath.Combine( folder.IsEmptyPath ? folder : folder.RemoveLastPart() )
                                             .AppendPart( p.LastPart[..^".csproj".Length] );
                candidates.Add( (r, p, worldPath) );
            }
        }
        bool success = true;
        var result = new List<(Repo Repo, NormalizedPath Path)>();
        foreach( var entry in entries )
        {
            var matches = candidates.Where( c => EndsWith( c.WorldPath, entry ) ).ToList();
            if( matches.Count == 0 )
            {
                monitor.Error( $"No project matches '{entry}'." );
                success = false;
            }
            else if( matches.Count > 1 )
            {
                monitor.Error( $"""
                                '{entry}' matches {matches.Count} projects: add folders before the name to choose one.
                                {matches.Select( m => m.WorldPath.Path ).Concatenate( Environment.NewLine )}
                                """ );
                success = false;
            }
            else if( !result.Contains( (matches[0].Repo, matches[0].Path) ) )
            {
                result.Add( (matches[0].Repo, matches[0].Path) );
            }
        }
        return success ? result : null;

        static bool EndsWith( NormalizedPath path, NormalizedPath suffix )
        {
            int offset = path.Parts.Count - suffix.Parts.Count;
            if( offset < 0 ) return false;
            for( int i = 0; i < suffix.Parts.Count; ++i )
            {
                if( !string.Equals( path.Parts[offset + i], suffix.Parts[i], StringComparison.OrdinalIgnoreCase ) ) return false;
            }
            return true;
        }
    }

    // For each found project "X" that is not itself a test project, adds the project "X.Tests" of the same repository
    // (found by its name, wherever it is) when it exists and is not already found.
    static bool AddTestProjects( IActivityMonitor monitor,
                                 List<(Repo Repo, NormalizedPath Path)> found,
                                 List<(Repo Repo, NormalizedPath Path)> added )
    {
        bool success = true;
        foreach( var (repo, path) in found.ToList() )
        {
            var name = path.LastPart[..^".csproj".Length];
            if( name.EndsWith( ".Tests", StringComparison.OrdinalIgnoreCase ) ) continue;
            var paths = MutableSolution.ReadProjectPaths( monitor, repo );
            if( paths == null ) return false;
            var testFile = name + ".Tests.csproj";
            var tests = paths.Where( p => p.LastPart.Equals( testFile, StringComparison.OrdinalIgnoreCase ) ).ToList();
            if( tests.Count > 1 )
            {
                monitor.Error( $"""
                                '{repo.DisplayPath}' has {tests.Count} projects named '{name}.Tests': name the one to move instead of using --with-tests.
                                {tests.Select( t => t.Path ).Concatenate( Environment.NewLine )}
                                """ );
                success = false;
            }
            else if( tests.Count == 1 && !found.Contains( (repo, tests[0]) ) )
            {
                found.Add( (repo, tests[0]) );
                added.Add( (repo, tests[0]) );
            }
        }
        return success;
    }

    static bool CheckDestinations( IActivityMonitor monitor, Repo targetRepo, List<MovedProject> moved )
    {
        bool success = true;
        foreach( var m in moved )
        {
            if( m.Source == targetRepo && m.NewPath.Path.Equals( m.OldPath.Path, StringComparison.OrdinalIgnoreCase ) )
            {
                monitor.Error( $"Project '{m.Name}' is already in '{targetRepo.DisplayPath}/{m.NewFolder}'." );
                success = false;
            }
            else if( m.Source == targetRepo && m.NewFolder.StartsWith( m.OldFolder, strict: true ) )
            {
                monitor.Error( $"Project '{m.Name}' can't be moved inside its own folder '{m.OldFolder}'." );
                success = false;
            }
            else if( Directory.Exists( targetRepo.WorkingFolder.Combine( m.NewFolder ) ) )
            {
                monitor.Error( $"Folder '{targetRepo.DisplayPath}/{m.NewFolder}' already exists." );
                success = false;
            }
            if( m.Name.EndsWith( ".Tests", StringComparison.OrdinalIgnoreCase )
                && !m.NewFolder.Parts.Any( p => p.Equals( "Tests", StringComparison.OrdinalIgnoreCase ) ) )
            {
                var hint = moved.All( o => o.Name.EndsWith( ".Tests", StringComparison.OrdinalIgnoreCase ) )
                            ? " To move test projects alone, run the move from the 'Tests/' folder of the target."
                            : "";
                monitor.Error( $"Test project '{m.Name}' would land in '{m.NewFolder}': a test project must be in a 'Tests' folder.{hint}" );
                success = false;
            }
        }
        return success;
    }

    // The highest Major.Minor.Patch of a repository's versions (fake, CI and local ones included): what a moved
    // package must never be produced again in.
    SVersion GetHighestVersion( IActivityMonitor monitor, Repo repo )
    {
        var max = _versionTag.Get( monitor, repo ).AllVersions.Select( v => v.Version ).Max();
        return max == null ? SVersion.ZeroVersion : SVersion.Create( max.Major, max.Minor, max.Patch );
    }

    // The version written in a new package reference: the one the consumer repository already uses for this package if any
    // (one version per package in a repository), otherwise the target's starting version for a package it receives, or the
    // producer's highest version. A build must follow the move: it rewrites these versions.
    SVersion GetReferenceVersion( IActivityMonitor monitor,
                                  Dictionary<Repo, List<MutableProject>> repoProjects,
                                  Repo consumer,
                                  string packageId,
                                  Repo producer,
                                  Repo targetRepo,
                                  SVersion? startVersion )
    {
        if( repoProjects.TryGetValue( consumer, out var list ) )
        {
            foreach( var p in list )
            {
                foreach( var (e, id) in p.PackageReferences )
                {
                    var text = (string?)e.Attribute( CKli.Core.XNames.Version );
                    if( id.Equals( packageId, StringComparison.OrdinalIgnoreCase )
                        && text != null
                        && SVersion.TryParse( text, out var v ) )
                    {
                        return v;
                    }
                }
            }
        }
        return startVersion != null && producer == targetRepo
                ? startVersion
                : GetHighestVersion( monitor, producer );
    }
}
