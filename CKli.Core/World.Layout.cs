using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace CKli.Core;

sealed partial class World
{
    /// <summary>
    /// Gets whether <see cref="AddRepository"/>, <see cref="RemoveRepository"/> or <see cref="XifLayout"/> can be called.
    /// </summary>
    public bool CanChangeLayout => _firstRepo == null;

    /// <summary>
    /// Adds a new repository.
    /// <para>
    /// This can be called only if <see cref="CanChangeLayout"/> is true otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// <list type="number">
    ///     <item>The uri must be a valid absolute url.</item>
    ///     <item>No existing repository must exist with the same repository name.</item>
    ///     <item>The path must be <see cref="WorldRoot"/> or starts with it (it should not end with the repository name).</item>
    ///     <item>The path must not be below any exisiting repository.</item>
    ///     <item>The repository is cloned.</item>
    ///     <item>The world definition file is updated, saved and comitted in the Stack repository.</item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repositoryUri">The repository url.</param>
    /// <param name="folderPath">The absolute folder path inside <see cref="WorldRoot"/>.</param>
    /// <returns>True on success, false on error.</returns>
    public bool AddRepository( IActivityMonitor monitor, Uri repositoryUri, NormalizedPath folderPath )
    {
        Throw.CheckState( CanChangeLayout );
        return _name.AddRepository( monitor, repositoryUri, folderPath );
    }

    /// <summary>
    /// Removes a repository.
    /// <para>
    /// This can be called only if <see cref="CanChangeLayout"/> is true otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="nameOrUrl">The repository name or url.</param>
    /// <returns>True on success, false on error.</returns>
    public bool RemoveRepository( IActivityMonitor monitor, string nameOrUrl )
    {
        Throw.CheckState( CanChangeLayout );
        return _name.RemoveRepository( monitor, nameOrUrl );
    }

    /// <summary>
    /// Tries to fix the physical layout of the world. This doesn't change this world's <see cref="Layout"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="deleteAliens">True to delete repositories that are not defined in this world.</param>
    /// <param name="newClones">Outputs the Repo that have been cloned.</param>
    /// <returns>True on success, false on error.</returns>
    public bool FixLayout( IActivityMonitor monitor, bool deleteAliens, out List<Repo>? newClones )
    {
        newClones = null;
        var actions = CreateFixOrXifLayoutRoadMap( monitor, fix: true );
        if( actions == null ) return false;
        if( actions.Count == 0 ) return true;

        var message = $"""
            Updating '{_name.FullName}' folders with:
            {string.Join( Environment.NewLine, actions.Where( a => a is not Suppress).Select( a => a.ToString( _name.WorldRoot.Parts.Count ) ) )}
            """;
        using var _ = monitor.OpenInfo( message );

        var potentiallyEmptyFolders = new HashSet<NormalizedPath>();

        if( !ExecuteMoves( monitor, this, actions, potentiallyEmptyFolders ) )
        {
            monitor.Error( "Error while fixing layout. This must be manually fixed." );
            return false;
        }
        if( !ExecuteClones( monitor, this, actions, out newClones ) )
        {
            return false;
        }
        ExecuteSuppr( monitor, this, deleteAliens, actions, potentiallyEmptyFolders );

        potentiallyEmptyFolders.Remove( _name.WorldRoot );
        if( potentiallyEmptyFolders.Count > 0 )
        {
            DeletePotentiallyEmptyFolders( monitor, potentiallyEmptyFolders );
        }
        return FixedLayout == null || SafeRaiseEvent( monitor, FixedLayout, new FixedAllLayoutEventArgs( monitor, this, newClones ) );

        static bool ExecuteMoves( IActivityMonitor monitor, World world, List<LayoutAction> actions, HashSet<NormalizedPath> potentiallyEmptyFolders )
        {
            bool success = true;
            foreach( var m in actions.OfType<Move>() )
            {
                success &= FileHelper.TryMoveFolder( monitor, m.Path, m.NewPath, potentiallyEmptyFolders );
            }
            return success;
        }

        static bool ExecuteClones( IActivityMonitor monitor,
                                   World world,
                                   List<LayoutAction> actions,
                                   [NotNullWhen( true )] out List<Repo>? newClones )
        {
            bool success = true;
            newClones = null;
            foreach( var c in actions.OfType<Clone>() )
            {
                Throw.DebugAssert( "Since we must Clone, the cached repository is missing.", world._cachedRepositories[c.Uri.ToString()] == null );
                Throw.DebugAssert( "Since we must Clone, the cached repository is missing.", world._cachedRepositories[c.Path] == null );
                var gitKey = new GitRepositoryKey( world._stackRepository.SecretsStore, c.Uri, world._stackRepository.IsPublic );
                var cloned = GitRepository.Clone( monitor, gitKey, c.Path, c.Path.LastPart );
                if( cloned == null )
                {
                    success = false;
                }
                else
                {
                    var repo = world.CreateRepo( world.Layout.IndexOf( e => e.Path == c.Path ), cloned );
                    newClones ??= new List<Repo>();
                    newClones.Add( repo );
                }
            }
            return success;
        }

        static void ExecuteSuppr( IActivityMonitor monitor,
                                  World world,
                                  bool deleteAliens,
                                  List<LayoutAction> actions,
                                  HashSet<NormalizedPath> potentiallyEmptyFolders )
        {
            var toSuppr = actions.OfType<Suppress>().ToList();
            if( toSuppr.Count > 0 )
            {
                if( deleteAliens )
                {
                    using( monitor.OpenInfo( $"Repositories '{toSuppr.Select( s => s.Uri.ToString() ).Concatenate( "', '" )}' do not belong to this world. Trying to delete them." ) )
                    {
                        foreach( var suppr in actions.OfType<Suppress>() )
                        {
                            if( FileHelper.DeleteFolder( monitor, suppr.Path ) )
                            {
                                potentiallyEmptyFolders.Add( suppr.Path.RemoveLastPart() );
                            }
                        }
                    }
                }
                else
                {
                    monitor.Info( $"""
                    Following repositories don't belong to this world:
                        - {toSuppr.Select( s => $"{s.Uri} in '{s.Path}'" ).Concatenate( Environment.NewLine )}
                    They should be deleted.
                    """ );
                }
            }
        }

        static void DeletePotentiallyEmptyFolders( IActivityMonitor monitor, HashSet<NormalizedPath> potentiallyEmptyFolders )
        {
            using( monitor.OpenDebug( $"Trying to remove {potentiallyEmptyFolders.Count} potentially empty folders." ) )
            {
                int count = 0;
                foreach( var path in potentiallyEmptyFolders )
                {
                    try
                    {
                        Directory.Delete( path );
                        monitor.Debug( $"Removed empty folder '{path}'." );
                        ++count;
                    }
                    catch( Exception ex )
                    {
                        monitor.Debug( $"Failed to remove '{path}'.", ex );
                    }
                }
                if( count > 0 ) monitor.Trace( $"Removed {count} empty folders." );
            }
        }
    }

    /// <summary>
    /// Updates the layout of the current world from existing folders and repositories.
    /// <para>
    /// This can be called only if <see cref="CanChangeLayout"/> is true otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </summary>
    /// <param name="monitor"></param>
    /// <returns></returns>
    public bool XifLayout( IActivityMonitor monitor )
    {
        Throw.CheckState( CanChangeLayout );
        var actions = CreateFixOrXifLayoutRoadMap( monitor, fix: false );
        if( actions == null ) return false;
        if( actions.Count == 0 ) return true;
        var message = $"""
            Updating '{_name.XmlDescriptionFilePath.LastPart}' in '{_stackRepository.GitDisplayPath}' with:
            {string.Join( Environment.NewLine, actions.Select( a => a.ToString( _name.WorldRoot.Parts.Count ) ) )}
            """;
        using var _ = monitor.OpenInfo( message );
        using( _definitionFile.StartEdit() )
        {
            foreach( var a in actions )
            {
                switch( a )
                {
                    case Move m:
                        if( !_definitionFile.RemoveRepository( monitor, m.Uri, removeEmptyFolder: false ) )
                        {
                            return false;
                        }
                        _definitionFile.AddRepository( m.NewPath, m.NewPath.Parts.Skip( _name.WorldRoot.Parts.Count ), m.Uri );
                        break;
                    case Clone c:
                        _definitionFile.AddRepository( c.Path, c.Path.Parts.Skip( _name.WorldRoot.Parts.Count ), c.Uri );
                        break;
                    case Suppress s:
                        if( !_definitionFile.RemoveRepository( monitor, s.Uri, removeEmptyFolder: false ) )
                        {
                            return false;
                        }
                        break;
                }
            }
            _definitionFile.RemoveEmptyFolders();
        }
        return _name.SaveAndCommitDefinitionFile( monitor, "Before Xif.", message );
    }

    abstract record LayoutAction( NormalizedPath Path, Uri Uri )
    {
        public abstract string ToString( int skipedPathParts );
    }
    sealed record Clone( NormalizedPath Path, Uri Uri ) : LayoutAction( Path, Uri )
    {
        public override string ToString( int skipedPathParts ) => $"New repository '{Path.Parts.Skip( skipedPathParts ).Concatenate( '/' )}' ({Uri})";
    }
    sealed record Suppress( NormalizedPath Path, Uri Uri ) : LayoutAction( Path, Uri )
    {
        public override string ToString( int skipedPathParts ) => $"Suppress '{Path.Parts.Skip( skipedPathParts ).Concatenate( '/' )}' ({Uri})";
    }
    sealed record Move( NormalizedPath Path, Uri Uri, NormalizedPath NewPath, bool FixedCase ) : LayoutAction( Path, Uri )
    {
        public override string ToString( int skipedPathParts ) => $"{(FixedCase ? "Fixed case in" : "Moved")} '{Path.Parts.Skip( skipedPathParts ).Concatenate( '/' )}' to '{NewPath.Parts.Skip( skipedPathParts ).Concatenate( '/' )}'.";
    }

    List<LayoutAction>? CreateFixOrXifLayoutRoadMap( IActivityMonitor monitor, bool fix )
    {
        var physicalLayout = ReadPhysicalLayout( monitor, !fix );
        if( physicalLayout == null ) return null;
        var actions = CreateFixOrXifLayoutRoadMap( physicalLayout, _layout, fix );
        if( actions.Count == 0 )
        {
            monitor.Info( $"Layout '{_stackRepository.GitDisplayPath}/{_name.XmlDescriptionFilePath.LastPart}' is up-to-date with folders and repositories in '{_name.WorldRoot}'." );
        }
        return actions;
    }

    static List<LayoutAction> CreateFixOrXifLayoutRoadMap( Dictionary<Uri, NormalizedPath> physicalLayout,
                                                           IReadOnlyList<(NormalizedPath Path, Uri Uri)> logicalLayout,
                                                           bool fix )
    {

        var result = new List<LayoutAction>();
        foreach( var (path, uri) in logicalLayout )
        {
            if( !physicalLayout.TryGetValue( uri, out var exist ) )
            {
                result.Add( fix ? new Clone( path, uri ) : new Suppress( path, uri ) );
            }
            else
            {
                if( exist != path )
                {
                    bool fixedCase = exist.Path.Equals( path, StringComparison.OrdinalIgnoreCase );
                    var to = path;
                    if( !fix )
                    {
                        (exist, to) = (to, exist.RemoveLastPart());
                    }
                    result.Add( new Move( exist, uri, to, fixedCase ) );
                }
                physicalLayout.Remove( uri );
            }
        }
        foreach( var (uri, path) in physicalLayout )
        {
            result.Add( fix ? new Suppress( path, uri ) : new Clone( path, uri ) );
        }
        return result;
    }

    Dictionary<Uri, NormalizedPath>? ReadPhysicalLayout( IActivityMonitor monitor, bool forXif )
    {
        var result = new Dictionary<Uri, NormalizedPath>( GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer );
        var nativeWorldRootPath = Path.GetFullPath( _name.WorldRoot ) + Path.DirectorySeparatorChar;
        if( Read( monitor, this, nativeWorldRootPath, nativeWorldRootPath, result, forXif ) )
        {
            return result;
        }
        return null;

        static bool Read( IActivityMonitor monitor,
                          World world,
                          string path,
                          string nativeWorldRootPath,
                          Dictionary<Uri, NormalizedPath> result,
                          bool forXif )
        {
            bool success = true;
            foreach( var p in Directory.EnumerateDirectories( path, "*", SearchOption.TopDirectoryOnly ) )
            {
                Throw.DebugAssert( p.StartsWith( nativeWorldRootPath ) );
                var subFolder = p.AsSpan( nativeWorldRootPath.Length );
                if( subFolder.Equals( StackRepository.PublicStackName, StringComparison.OrdinalIgnoreCase )
                    || subFolder.Equals( StackRepository.PrivateStackName, StringComparison.OrdinalIgnoreCase ) )
                {
                    // The Stack repository must not appear in the physical layout.
                    continue;
                }
                var pGitConfig = p + "/.git/config";
                if( File.Exists( pGitConfig ) )
                {
                    var uri = ReadOriginUri( monitor, world, pGitConfig, forXif );
                    if( uri != null )
                    {
                        if( result.TryGetValue( uri, out var exists ) )
                        {
                            monitor.Error( $"""
                                Duplicate repository found for origin '{uri}'.
                                Cloned at '{exists}'
                                And at '{new NormalizedPath( p )}'.
                                Please remove one of them before retrying.
                                """ );
                            success = false;
                        }
                        else
                        {
                            var nP = new NormalizedPath( p );
                            if( nP.LastPart.Equals( StackRepository.PublicStackName, StringComparison.OrdinalIgnoreCase )
                                || nP.LastPart.Equals( StackRepository.PrivateStackName, StringComparison.OrdinalIgnoreCase ) )
                            {
                                monitor.Warn( $"""
                                              Found a '{nP.LastPart}' folder inside world '{world.Name}' at '{nP}'.
                                              A stack cannot be inside another stack.
                                              This is ignored but should be fixed by removing this folder.
                                              """ );
                            }
                            else
                            {
                                result.Add( uri, nP );
                            }
                        }
                    }
                    else
                    {
                        success = false;
                    }
                }
                else
                {
                    success &= Read( monitor, world, p, nativeWorldRootPath, result, forXif );
                }
            }
            return success;

            static Uri? ReadOriginUri( IActivityMonitor monitor, World world, string gitConfigPath, bool forXif )
            {
                // Really crappy... But I want this to be fast.
                // Will see if it happens to be an issue.
                const string remoteOriginSection = "[remote \"origin\"]";
                var config = File.ReadAllText( gitConfigPath ).AsSpan();
                var idx = config.IndexOf( remoteOriginSection );
                if( idx >= 0 )
                {
                    Throw.DebugAssert( remoteOriginSection.Length == 17 );
                    idx += 17;
                    var maxIdx = config.Slice( idx ).IndexOf( '[' );
                    if( maxIdx < 0 ) maxIdx = config.Length;
                    var s = config.Slice( idx, maxIdx );
                    idx = s.IndexOf( "url = " );
                    if( idx >= 0 )
                    {
                        s = s.Slice( idx + 6 );
                        maxIdx = s.IndexOfAny( "\r\n\t " );
                        if( maxIdx < 0 ) maxIdx = s.Length;
                        var sUri = new string( s.Slice( 0, maxIdx ) );
                        if( Uri.TryCreate( sUri, UriKind.Absolute, out var uri ) )
                        {
                            return CheckUri( monitor, uri, gitConfigPath, forXif );
                        }
                    }
                }
                monitor.Debug( $"Unable to read remote url from '{gitConfigPath}'. Using Git api." );

                var workingFolder = Path.GetDirectoryName( Path.GetDirectoryName( gitConfigPath ) );
                Throw.DebugAssert( workingFolder != null );
                // We use the cache if possible: the Repo will potentially be reused.
                var repo = world.TryLoadDefinedGitRepository( monitor, workingFolder, mustExist: false );
                if( repo != null )
                {
                    Throw.DebugAssert( "Already normalized.", GitRepositoryKey.CheckAndNormalizeRepositoryUrl( repo.OriginUrl ) == repo.OriginUrl );
                    return repo.OriginUrl;
                }
                // No luck: there is a layout issue!
                var r = GitRepository.OpenWorkingFolder( monitor, workingFolder, warnOnly: false );
                if( r != null )
                {
                    r.Value.Repository.Dispose();
                    return CheckUri( monitor, r.Value.OriginUrl, gitConfigPath, forXif );
                }
                return null;
            }
        }

        static Uri? CheckUri( IActivityMonitor monitor, Uri? uri, string gitConfigPath, bool forXif )
        {
            uri = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( monitor, uri, out var repoName );
            if( uri == null )
            {
                monitor.Error( $"Invalid origin url in '{gitConfigPath}'." );
            }
            else if( forXif )
            {
                Throw.DebugAssert( repoName != null );
                Throw.DebugAssert( gitConfigPath.EndsWith( "/.git/config" ) && "/.git/config".Length == 12 );
                int idxExpectedRepoName = gitConfigPath.Length - 12 - repoName.Length;
                if( idxExpectedRepoName < 0
                    || !gitConfigPath.AsSpan( idxExpectedRepoName, repoName.Length ).Equals( repoName, StringComparison.OrdinalIgnoreCase ) )
                {
                    monitor.Error( $"""
                        Invalid parent folder name for repository '{repoName}'. The lats folder name of the path must end the repository name.
                        Path: {gitConfigPath[..12] }
                        The current layout is not valid and must be fixed.
                        """ );
                    return null;
                }
            }
            return uri;
        }

    }

}
