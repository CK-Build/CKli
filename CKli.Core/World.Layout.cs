using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace CKli.Core;

sealed partial class World
{
    abstract record LayoutAction( NormalizedPath Path, Uri Uri );
    sealed record Clone( NormalizedPath Path, Uri Uri ) : LayoutAction( Path, Uri );
    sealed record Suppress( NormalizedPath Path, Uri Uri ) : LayoutAction( Path, Uri );
    sealed record Move( NormalizedPath Path, Uri Uri, NormalizedPath NewPath, bool FixedCase ) : LayoutAction( Path, Uri );

    /// <summary>
    /// Tries to fix the physical layout fo the world.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="deleteAliens">True to delete repositories that are not defined in this world.</param>
    /// <param name="newClones">Outputs the GitRepository that have been cloned.</param>
    /// <returns>True on success, false on error.</returns>
    public bool FixLayout( IActivityMonitor monitor, bool deleteAliens, [NotNullWhen(true)] out List<GitRepository>? newClones )
    {
        newClones = null;
        var physicalLayout = ReadPhysicalLayout( monitor );
        if( physicalLayout == null ) return false;
        var actions = CreateRoadMap( physicalLayout, _layout );

        int commonPathLength = _stackRepository.StackRoot.Path.Length;

        if( !ExecuteMoves( monitor, actions, commonPathLength ) )
        {
            monitor.Error( "Error while updating repository location. This must be manually fixed." );
            return false;
        }
        if( !ExecuteClones( monitor, this, actions, out newClones ) )
        {
            return false;
        }
        ExecuteSuppr( monitor, deleteAliens, actions, commonPathLength );
        return true;

        static bool ExecuteMoves( IActivityMonitor monitor, List<LayoutAction> actions, int commonPathLength )
        {
            bool success = true;
            foreach( var m in actions.OfType<Move>() )
            {
                if( FileHelper.TryMoveFolder( monitor, m.Path, m.NewPath, commonPathLength ) )
                {
                    monitor.Info( $"{(m.FixedCase ? "Fixed case in" : "Moved")} '{m.Path.Path.AsSpan( commonPathLength )}' to '{m.NewPath.Path.AsSpan( commonPathLength )}'." );
                }
                else
                {
                    success = false;
                }
            }

            return success;
        }

        static bool ExecuteClones( IActivityMonitor monitor,
                                   World world,
                                   List<LayoutAction> actions,
                                   [NotNullWhen( true )] out List<GitRepository>? newClones )
        {
            bool success = false;
            newClones = null;
            var cache = world._cachedRepositories;
            foreach( var c in actions.OfType<Clone>() )
            {
                Throw.DebugAssert( "Since we must Clone, the cached repository is missing.", cache[c.Uri.ToString()] == null );
                Throw.DebugAssert( "Since we must Clone, the cached repository is missing.", cache[c.Path] == null );
                var gitKey = new GitRepositoryKey( world._stackRepository.SecretsStore, c.Uri, world._stackRepository.IsPublic );
                var cloned = GitRepository.Clone( monitor, gitKey, c.Path, c.Path.LastPart );
                if( cloned == null )
                {
                    success = false;
                }
                else
                {
                    newClones ??= new List<GitRepository>();
                    newClones.Add( cloned );
                    cache[c.Uri.ToString()] = cloned;
                    cache[c.Path] = cloned;
                }
            }
            return success;
        }

        static void ExecuteSuppr( IActivityMonitor monitor, bool deleteAliens, List<LayoutAction> actions, int commonPathLength )
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
                            FileHelper.TryDeleteFolder( monitor, suppr.Path );
                        }
                    }
                }
                else
                {
                    monitor.Info( $"""
                    Following repositories do not belong to this world:
                        - {toSuppr.Select( s => $"{s.Uri.ToString()} in '{s.Path.Path.AsSpan( commonPathLength )}'" ).Concatenate( Environment.NewLine )}
                    They should be deleted.
                    """ );
                }
            }
        }
    }

    static List<LayoutAction> CreateRoadMap( Dictionary<Uri, NormalizedPath> physicalLayout,
                                             IReadOnlyList<(NormalizedPath Path, Uri Uri)> target )
    {

        var result = new List<LayoutAction>();
        foreach( var (path, uri) in target )
        {
            if( !physicalLayout.TryGetValue( uri, out var exist ) )
            {
                result.Add( new Clone( path, uri ) );
            }
            else
            {
                if( exist.Path.Equals( path, StringComparison.OrdinalIgnoreCase ) )
                {
                    result.Add( new Move( exist, uri, path, true ) );
                }
                else
                {
                    result.Add( new Move( exist, uri, path, false ) );
                }
                physicalLayout.Remove( uri );
            }
        }
        foreach( var toSuppress in physicalLayout )
        {
            result.Add( new Suppress( toSuppress.Value, toSuppress.Key ) );
        }
        return result;
    }

    Dictionary<Uri, NormalizedPath>? ReadPhysicalLayout( IActivityMonitor monitor )
    {
        var result = new Dictionary<Uri, NormalizedPath>( GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer );
        if( Read( monitor, this, _name.Root, result ) )
        {
            return result;
        }
        return null;

        static bool Read( IActivityMonitor monitor,
                          World world,
                          string path,
                          Dictionary<Uri, NormalizedPath> result )
        {
            bool success = true;
            foreach( var p in Directory.EnumerateDirectories( path, "*", SearchOption.TopDirectoryOnly ) )
            {
                var pGitConfig = p + "/.git/config";
                if( File.Exists( pGitConfig ) )
                {
                    var uri = ReadOriginUri( monitor, world, pGitConfig );
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
                        else result.Add( uri, p );
                    }
                    else
                    {
                        success = false;
                    }
                }
                else
                {
                    success &= Read( monitor, world, p, result );
                }
            }
            return success;

            static Uri? ReadOriginUri( IActivityMonitor monitor, World world, string gitConfigPath )
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
                    var s = config.Slice( idx, maxIdx - idx );
                    idx = s.IndexOf( "url = " );
                    if( idx >= 0 )
                    {
                        maxIdx = s.IndexOfAny( "\r\n\t " );
                        if( maxIdx < 0 ) maxIdx = s.Length;
                        var sUri = new string( s.Slice( idx, maxIdx - idx ) );
                        if( Uri.TryCreate( sUri, UriKind.Absolute, out var uri ) )
                        {
                            return uri;
                        }
                    }
                }
                monitor.Debug( $"Unable to read remote url from '{gitConfigPath}'. Using Git api." );

                var workingFolder = Path.GetDirectoryName( Path.GetDirectoryName( gitConfigPath ) );
                Throw.DebugAssert( workingFolder != null );
                // We use the cache if possible: the GitRepository will potentially be reused.
                var gitRepository = world.TryLoadDefinedGitRepository( monitor, workingFolder, mustExist: false );
                if( gitRepository != null )
                {
                    return gitRepository.OriginUrl;
                }
                // No luck: there is a layout issue!
                var r = GitRepository.OpenWorkingFolder( monitor, workingFolder, warnOnly: false );
                if( r != null )
                {
                    r.Value.Repository.Dispose();
                    return r.Value.OriginUrl;
                }
                return null;
            }
        }

    }

}
