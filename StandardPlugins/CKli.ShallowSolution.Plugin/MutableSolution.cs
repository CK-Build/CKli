using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Encapsulates a .slnx solution in file system directory (a checked out commit).
/// <para>
/// This handles migration from legacy .sln and change of solution name (if one and only
/// one .sln or .slnx exists at the root).
/// </para>
/// <para>
/// The supported operations are <see cref="UpdatePackages"/> (that saves the project files it changes) and the
/// edition of the solution's projects (<see cref="RemoveProject"/>, <see cref="AddProject"/>) that <see cref="Save"/>
/// saves.
/// </para>
/// </summary>
public sealed class MutableSolution
{
    readonly Repo _repo;
    readonly XDocument _solution;
    readonly NormalizedPath _solutionPath;
    bool _isDirty;

    MutableSolution( Repo repo, XDocument solution, NormalizedPath solutionPath )
    {
        _repo = repo;
        _solution = solution;
        _solutionPath = solutionPath;
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the paths of the ".csproj" files of the solution, relative to the repository's working folder.
    /// </summary>
    public IEnumerable<NormalizedPath> ProjectPaths => _solution.Root!.Descendants( XNames.Project )
                                                                       .Select( p => new NormalizedPath( (string?)p.Attribute( XNames.Path ) ) )
                                                                       .Where( p => !p.IsEmptyPath );

    /// <summary>
    /// Gets whether <see cref="RemoveProject"/> or <see cref="AddProject"/> changed the solution: <see cref="Save"/>
    /// must be called.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Removes a project from the solution. The solution folder that contained it is removed when it becomes empty,
    /// and so are its parent solution folders that contain nothing else.
    /// </summary>
    /// <param name="projectPath">The path of the ".csproj" file, relative to the repository's working folder.</param>
    /// <returns>True if the project has been removed, false if it was not in the solution.</returns>
    public bool RemoveProject( NormalizedPath projectPath )
    {
        var e = FindProject( projectPath );
        if( e == null ) return false;
        var folder = e.Parent;
        e.Remove();
        _isDirty = true;
        if( folder != null && folder.Name == XNames.Folder )
        {
            var name = (string?)folder.Attribute( XNames.Name );
            // A solution folder is "/A/B/": its parents are "/A/" (and the root).
            while( name != null && name.Length > 1 )
            {
                var f = FindFolder( name );
                if( f == null || f.HasElements ) break;
                // A folder that is the parent of another one is not empty.
                if( _solution.Root!.Elements( XNames.Folder )
                                   .Select( o => (string?)o.Attribute( XNames.Name ) )
                                   .Any( n => n != null
                                              && n.Length > name.Length
                                              && n.StartsWith( name, StringComparison.OrdinalIgnoreCase ) ) )
                {
                    break;
                }
                f.Remove();
                name = name[..(name.LastIndexOf( '/', name.Length - 2 ) + 1)];
            }
        }
        return true;
    }

    /// <summary>
    /// Adds a project to the solution, in the solution folder derived from its path: a project at the root of the
    /// repository (<c>Project/Project.csproj</c>) is at the root of the solution, <c>Tests/Project.Tests/Project.Tests.csproj</c>
    /// is in the "/Tests/" solution folder. The missing solution folders are created.
    /// </summary>
    /// <param name="projectPath">The path of the ".csproj" file, relative to the repository's working folder.</param>
    /// <returns>True if the project has been added, false if it was already in the solution.</returns>
    public bool AddProject( NormalizedPath projectPath )
    {
        if( FindProject( projectPath ) != null ) return false;
        var newProject = new XElement( XNames.Project, new XAttribute( XNames.Path, projectPath.Path ) );
        var folderPath = GetSolutionFolderPath( projectPath );
        XElement container;
        if( folderPath.IsEmptyPath )
        {
            container = _solution.Root!;
        }
        else
        {
            container = EnsureFolder( folderPath );
            // Its parent folders exist too.
            for( var p = folderPath.RemoveLastPart(); !p.IsEmptyPath; p = p.RemoveLastPart() )
            {
                EnsureFolder( p );
            }
        }
        // The projects are sorted by path in their container, after its other elements (the root's folders, a
        // folder's files).
        var after = container.Elements( XNames.Project )
                             .LastOrDefault( p => StringComparer.OrdinalIgnoreCase.Compare( (string?)p.Attribute( XNames.Path ), projectPath.Path ) < 0 );
        if( after != null )
        {
            after.AddAfterSelf( newProject );
        }
        else
        {
            var first = container.Elements( XNames.Project ).FirstOrDefault();
            if( first != null ) first.AddBeforeSelf( newProject );
            else container.Add( newProject );
        }
        _isDirty = true;
        return true;

        XElement EnsureFolder( NormalizedPath folderPath )
        {
            var name = '/' + folderPath.Path + '/';
            var f = FindFolder( name );
            if( f == null )
            {
                f = new XElement( XNames.Folder, new XAttribute( XNames.Name, name ) );
                // The folders are sorted by name, before the root's projects.
                var root = _solution.Root!;
                var after = root.Elements( XNames.Folder )
                                .LastOrDefault( o => StringComparer.OrdinalIgnoreCase.Compare( (string?)o.Attribute( XNames.Name ), name ) < 0 );
                if( after != null )
                {
                    after.AddAfterSelf( f );
                }
                else
                {
                    var first = root.Elements().FirstOrDefault();
                    if( first != null ) first.AddBeforeSelf( f );
                    else root.Add( f );
                }
            }
            return f;
        }
    }

    /// <summary>
    /// Reads the paths of the ".csproj" files of a repository's solution from its working folder, without any
    /// side effect (unlike <see cref="Create"/> that migrates a legacy solution file).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <returns>
    /// The paths relative to the repository's working folder (empty when the repository has no conventional
    /// ".slnx" file), or null on error.
    /// </returns>
    public static IReadOnlyList<NormalizedPath>? ReadProjectPaths( IActivityMonitor monitor, Repo repo )
    {
        var path = repo.WorkingFolder.AppendPart( repo.DisplayPath.LastPart + ".slnx" );
        if( !File.Exists( path ) ) return [];
        try
        {
            var doc = XDocument.Load( path );
            return doc.Root!.Descendants( XNames.Project )
                            .Select( p => new NormalizedPath( (string?)p.Attribute( XNames.Path ) ) )
                            .Where( p => !p.IsEmptyPath )
                            .ToList();
        }
        catch( Exception ex )
        {
            monitor.Error( $"While reading '{repo.DisplayPath}' solution.", ex );
            return null;
        }
    }

    /// <summary>
    /// Gets the solution folder of a project derived from its path: the folders that contain the project's folder.
    /// The empty path is the root of the solution.
    /// </summary>
    /// <param name="projectPath">The path of the ".csproj" file, relative to the repository's working folder.</param>
    /// <returns>The solution folder path ("Tests" for <c>Tests/Project.Tests/Project.Tests.csproj</c>).</returns>
    public static NormalizedPath GetSolutionFolderPath( NormalizedPath projectPath ) => projectPath.RemoveLastPart( Math.Min( 2, projectPath.Parts.Count ) );

    /// <summary>
    /// Saves the solution file if <see cref="IsDirty"/> is true.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Save( IActivityMonitor monitor )
    {
        if( _isDirty )
        {
            try
            {
                // The solution has been loaded with its whitespaces: they are dropped so that the saved file is
                // regularly indented, the edited elements included.
                foreach( var t in _solution.DescendantNodes().OfType<XText>().Where( t => string.IsNullOrWhiteSpace( t.Value ) ).ToList() )
                {
                    t.Remove();
                }
                XmlHelper.SafeSave( _solution, _solutionPath );
                _isDirty = false;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While saving '{_repo.DisplayPath}' solution.", ex );
                return false;
            }
        }
        return true;
    }

    XElement? FindProject( NormalizedPath projectPath ) => _solution.Root!.Descendants( XNames.Project )
                                                                       .FirstOrDefault( p => new NormalizedPath( (string?)p.Attribute( XNames.Path ) )
                                                                                               .Path.Equals( projectPath.Path, StringComparison.OrdinalIgnoreCase ) );

    XElement? FindFolder( string name ) => _solution.Root!.Elements( XNames.Folder )
                                                         .FirstOrDefault( f => string.Equals( (string?)f.Attribute( XNames.Name ), name, StringComparison.OrdinalIgnoreCase ) );

    /// <summary>
    /// Updating packages references is not that easy. "dotnet package update" is rather useless (it works only on a
    /// single project and when packages to update are not found, it "Updating outdated packages in ..." and we absolutely
    /// don't want this).
    /// <para>
    /// More complex solutions exist but... https://github.com/dotnet-outdated use msbuild and lockfile (project.assets.json)
    /// with NuGet.ProjectModel, https://github.com/RicoSuter/DNT use msbuild (and a bit of BuildAlyzer)
    /// and https://github.com/Buildalyzer/Buildalyzer is not really needed here.
    /// </para>
    /// <para>
    /// This implementation is brutal: we consider all the projects in the solution (that must be a .slnx) 
    /// and any Directory.Packages.props or Directory.Build.props files from their folder to the solution root and updates
    /// the PackageVersion or PackageReference elements.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="mapping">The packages mapping to apply.</param>
    /// <param name="updated">Optional collector of packages actually updated.</param>
    /// <returns>True on success, false on failure.</returns>
    public bool UpdatePackages( IActivityMonitor monitor, IPackageMapping mapping, PackageMapper? updated )
    {
        if( !mapping.IsEmpty )
        {
            var projectFiles = new List<(NormalizedPath Path, XElement Project)>();
            if( !CommonSolution.LoadAllProjectFiles( monitor,
                                                     new CheckedOutFileProvider( _repo.WorkingFolder ),
                                                     _solution.Root!,
                                                     LoadOptions.PreserveWhitespace,
                                                     ( monitor, path, project ) =>
                                                     {
                                                         projectFiles.Add( (path, project) );
                                                         return true;
                                                     } ) )
            {
                return false;
            }
            using( monitor.OpenInfo( $"Updating package versions in {projectFiles.Count} files." ) )
            {
                foreach( var p in projectFiles )
                {
                    if( !UpdateVersions( monitor, p.Path, p.Project, mapping, updated ) )
                    {
                        return false;
                    }
                }
                foreach( var p in projectFiles )
                {
                    XmlHelper.SafeSave( p.Project.Document!, _repo.WorkingFolder.Combine( p.Path ) );
                }
            }
        }
        return true;

        static bool UpdateVersions( IActivityMonitor monitor,
                                    string path,
                                    XElement projectRoot,
                                    IPackageMapping mapping,
                                    PackageMapper? updated )
        {
            // "Directory.Packages.props" (plural) is the MSBuild name and the one CommonSolution loads: a
            // singular spelling here silently sent that file to the <PackageReference> branch below, where it
            // has none, so a centrally managed <PackageVersion> was never updated and the alignment could
            // never converge.
            if( Path.GetFileName( path.AsSpan() ).Equals( "Directory.Packages.props", StringComparison.OrdinalIgnoreCase ) )
            {
                foreach( var e in projectRoot.Descendants( XNames.PackageVersion ) )
                {
                    var name = CommonSolution.GetIncludedName( monitor, path, e, LogLevel.Warn );
                    if( name == null ) continue;
                    // None: the mapping doesn't handle this identifier at all, reading its version is useless.
                    var mappingType = mapping.GetMappingType( name );
                    if( mappingType is PackageMappingType.None ) continue;
                    if( !UpdateVersion( monitor, path, e, name, XNames.Version, "Version", mapping, mappingType, updated ) )
                    {
                        return false;
                    }
                }
            }
            else
            {
                foreach( var e in projectRoot.Descendants( XNames.PackageReference ) )
                {
                    var name = CommonSolution.GetIncludedName( monitor, path, e, LogLevel.Warn );
                    if( name == null ) continue;
                    var mappingType = mapping.GetMappingType( name );
                    if( mappingType is PackageMappingType.None ) continue;
                    if( !UpdateVersion( monitor, path, e, name, XNames.VersionOverride, null, mapping, mappingType, updated ) )
                    {
                        return false;
                    }
                    if( !UpdateVersion( monitor, path, e, name, XNames.Version, null, mapping, mappingType, updated ) )
                    {
                        return false;
                    }
                }
            }
            return true;

            static bool UpdateVersion( IActivityMonitor monitor,
                                       string path,
                                       XElement e,
                                       string packageId,
                                       XName attributeName,
                                       string? attributeRequiredMessage,
                                       IPackageMapping map,
                                       PackageMappingType mappingType,
                                       PackageMapper? updated )
            {
               if( !CommonSolution.ReadVersionAttribute( monitor,
                                                         path,
                                                         e,
                                                         attributeName,
                                                         attributeRequiredMessage,
                                                         out XAttribute? a,
                                                         out SVersion? from ) )
                {
                    return false;
                }
                if( from != null )
                {
                    Throw.DebugAssert( a != null );
                    if( map.TryGetMappedVersion( packageId, from, out var to ) )
                    {
                        if( from != to )
                        {
                            a.SetValue( to.ToString() );
                            updated?.TryAdd( packageId, from, to );
                        }
                    }
                    else if( mappingType is PackageMappingType.Mapped )
                    {
                        // Only an exact mapping can say that a version it didn't map is a version it missed.
                        // For a KnownName one - a package whose configured bound this version satisfies, a
                        // solution whose last build must itself be rebuilt - a null is "leave it alone", and
                        // warning here made every build of a World with <Packages> bounds noisy.
                        monitor.Warn( $"""
                            Unhandled version in file '{path}':
                            {e}
                            The package '{packageId}/v{from}' has no mapping.
                            """ );
                    }
                }
                return true;
            }
        }

    }

    /// <summary>
    /// Creates a <see cref="MutableSolution"/> from the currently checked out repository's working folder.
    /// <para>
    /// This automatically handles "previous or legacy solution name" by considering single ".slnx" file (or ".sln"
    /// file - "dotnet sln migrate" is automatically called) and rename it to the
    /// conventional solution file name (that is the last part of <see cref="Repo.DisplayPath"/>).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <returns>The solution or null on error.</returns>
    public static MutableSolution? Create( IActivityMonitor monitor, Repo repo )
    {
        NormalizedPath slnOrSlnxPath;
        if( !EnsureSlnxFile( monitor, repo, out slnOrSlnxPath ) )
        {
            return null;
        }
        try
        {
            var doc = XDocument.Load( slnOrSlnxPath, LoadOptions.PreserveWhitespace );
            Throw.CheckData( "A .slnx file must contain a <Solution> root element.", doc.Root?.Name.LocalName == "Solution" );
            return new MutableSolution( repo, doc, slnOrSlnxPath );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading '{repo.DisplayPath}' solution.", ex );
        }
        return null;
    }

    static bool EnsureSlnxFile( IActivityMonitor monitor, Repo repo, out NormalizedPath path )
    {
        if( !FindSolutionFile( monitor, repo, out path, out var isLegacySln, out var mustRenameFile ) )
        {
            return false;
        }
        if( isLegacySln || mustRenameFile )
        {
            var target = repo.WorkingFolder.AppendPart( repo.DisplayPath.LastPart + ".slnx" );
            Throw.DebugAssert( "Or we won't be here.", !File.Exists( target ) );
            if( isLegacySln )
            {
                using( monitor.OpenInfo( $"Migrating '{path.LastPart}'." ) )
                {
                    Throw.DebugAssert( mustRenameFile );
                    var args = $"""sln "{path.LastPart}" migrate""";
                    var e = ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", args, repo.WorkingFolder );
                    if( e != 0 )
                    {
                        monitor.Error( $"Running 'dotnet {args}' failed with code '{e}'." );
                        return false;
                    }
                    // Successful migration:
                    // - The old .sln file can be deleted.
                    // - The path is now a .slnx file.
                    FileHelper.DeleteFile( monitor, path );
                    path = path.Path + 'x';
                    // This may be our target. Rather than testing path == target (case insensitive?), challenge the
                    // file system.
                    mustRenameFile = !File.Exists( target );
                }
            }
            if( mustRenameFile )
            {
                monitor.Info( $"Renaming '{path.LastPart}' to '{target.LastPart}'." );
                File.Move( path, target );
                path = target;
            }
        }
        return true;
    }

    static void WarnOnCaseIssue( IActivityMonitor monitor,
                                 NormalizedPath folder,
                                 string expectedName,
                                 string? displayFolder = null )
    {
        // Lookup from the parent with the name as a pattern.
        var actualFileName = Path.GetFileName( Directory.EnumerateFileSystemEntries( folder, expectedName ).First().AsSpan() );
        if( !actualFileName.Equals( expectedName, StringComparison.Ordinal ) )
        {
            monitor.Warn( $"Case issue for '{displayFolder ?? folder}/{actualFileName}': it should be '{expectedName}'." );
        }
    }


    static bool FindSolutionFile( IActivityMonitor monitor,
                                  Repo repo,
                                  out NormalizedPath path,
                                  out bool isLegacySln,
                                  out bool mustRenameFile )
    {
        isLegacySln = false;
        var expectedName = repo.DisplayPath.LastPart + ".slnx";
        var expectedPath = repo.WorkingFolder.AppendPart( expectedName );
        if( File.Exists( expectedPath ) )
        {
            WarnOnCaseIssue( monitor, repo.WorkingFolder, expectedName, repo.DisplayPath );
            path = expectedPath;
            mustRenameFile = false;
            return true;
        }
        monitor.Warn( $"Missing '{repo.DisplayPath}/{expectedName}'." );
        mustRenameFile = true;
        string? foundExactSln = null;
        string? foundSln1 = null;
        StringBuilder? extraSln = null;
        string? foundSlnx1 = null;
        StringBuilder? extraSlnx = null;
        var files = Directory.EnumerateFiles( repo.WorkingFolder );
        foreach( var f in files )
        {
            var ext = Path.GetExtension( f.AsSpan() );
            if( ext.Equals( ".sln", StringComparison.OrdinalIgnoreCase ) )
            {
                var name = Path.GetFileNameWithoutExtension( f.AsSpan() );
                if( name.Equals( repo.DisplayPath.LastPart, StringComparison.OrdinalIgnoreCase ) )
                {
                    foundExactSln = f;
                    break;
                }
                else
                {
                    if( foundSln1 == null )
                    {
                        foundSln1 = f;
                    }
                    else
                    {
                        if( extraSln == null )
                        {
                            extraSln = new StringBuilder( f );
                        }
                        else
                        {
                            extraSln.AppendLine().Append( f );
                        }
                    }
                }
            }
            else if( ext.Equals( ".slnx", StringComparison.OrdinalIgnoreCase ) )
            {
                if( foundSlnx1 == null )
                {
                    foundSlnx1 = f;
                }
                else
                {
                    if( extraSlnx == null )
                    {
                        extraSlnx = new StringBuilder( f );
                    }
                    else
                    {
                        extraSlnx.AppendLine().Append( f );
                    }
                }
            }
        }
        if( foundExactSln != null && foundSlnx1 != null )
        {
            // Ambiguous.
            monitor.Error( $"Found both '{foundExactSln}' (right naming) and '{foundSlnx1}' (expected .slnx format) in '{repo.DisplayPath}'." );
            path = default;
            return false;
        }
        // First: the exact .sln name (regular legacy case).
        if( foundExactSln != null )
        {
            monitor.Warn( $"Found legacy solution file '{Path.GetFileName( foundExactSln.AsSpan() )}'." );
            path = foundExactSln;
            isLegacySln = true;
            return true;
        }
        // Now we should have at least a .sln or a .slnx, otherwise we cannot do anything.
        if( foundSln1 == null && foundSlnx1 == null )
        {
            // No candidate.
            monitor.Error( $"Unable to find any .slnx or .sln file in '{repo.DisplayPath}'." );
            path = default;
            return false;
        }
        // Second: the .slnx file (unless there are more than one).
        if( foundSlnx1 != null )
        {
            if( extraSlnx != null )
            {
                monitor.Error( $"""
                    Ambiguous .slnx files in '{repo.DisplayPath}'. Cannot choose between:
                    {foundSlnx1}
                    {extraSlnx}
                    """ );
                path = default;
                return false;
            }
            monitor.Warn( $"Expecting '{expectedName}'. Considering single '{Path.GetFileName( foundSlnx1.AsSpan() )}' file." );
            path = foundSlnx1;
            return true;
        }
        // Third: the .sln file (unless there are more than one).
        Throw.DebugAssert( foundSln1 != null );
        if( extraSln != null )
        {
            monitor.Error( $"""
                Ambiguous .sln files in '{repo.DisplayPath}'. Cannot choose between:
                {foundSln1}
                {extraSln}
                """ );
            path = default;
            return false;
        }
        monitor.Warn( $"Expecting '{expectedName}'. Considering single legacy '{Path.GetFileName( foundSln1.AsSpan() )}' file." );
        path = foundSln1;
        return true;
    }

}



