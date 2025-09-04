using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace CKli;

/// <summary>
/// A world layout is basically a reaonly list of (NormalizedPath,Uri) tuples sorted by the path.
/// We don't model it more than that.
/// This static class provides the helpers that handle them.
/// </summary>
static class WorldLayout
{
    public abstract record LayoutAction( NormalizedPath Path, Uri Uri );
    public sealed record Clone( NormalizedPath Path, Uri Uri ) : LayoutAction( Path, Uri );
    public sealed record Noop( NormalizedPath Path, Uri Uri ) : LayoutAction( Path, Uri );
    public sealed record Suppress( NormalizedPath Path, Uri Uri ) : LayoutAction( Path, Uri );
    public sealed record Move( NormalizedPath Path, Uri Uri, NormalizedPath NewPath, bool FixedCase ) : LayoutAction( Path, Uri );

    public static bool ExecuteRoadMap( IActivityMonitor monitor,
                                       StackRepository stack,
                                       ISecretsStore secretsStore,
                                       IReadOnlyList<LayoutAction> actions )
    {
        bool success = true;
        int commonPathLength = stack.Path.Path.Length;
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
        if( !success )
        {
            monitor.Error( "Error while updating repository location. Skipping all pull from remotes." );
            return false;
        }

    }
    public static List<LayoutAction>? CreateRoadMap( IActivityMonitor monitor,
                                                     NormalizedPath rootPath,
                                                     IReadOnlyList<(NormalizedPath Path, Uri Uri)> target )
    {

        var current = ReadPhysicalLayout( monitor, rootPath );
        if( current == null ) return null;
        var result = new List<LayoutAction>();
        foreach( var (path, uri) in target )
        {
            if( !current.TryGetValue( uri, out var exist ) )
            {
                result.Add( new Clone( path, uri ) );
            }
            else
            {
                if( exist == path )
                {
                    result.Add( new Noop( path, uri ) );
                }
                else if( exist.Path.Equals( path, StringComparison.OrdinalIgnoreCase ) )
                {
                    result.Add( new Move( exist, uri, path, true ) );
                }
                else
                {
                    result.Add( new Move( exist, uri, path, false ) );
                }
                current.Remove( uri );
            }
        }
        foreach( var toSuppress in current )
        {
            result.Add( new Suppress( toSuppress.Value, toSuppress.Key ) );
        }
        return result;
    }

    static Dictionary<Uri, NormalizedPath>? ReadPhysicalLayout( IActivityMonitor monitor, NormalizedPath path )
    {
        var result = new Dictionary<Uri, NormalizedPath>( GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer );
        if( Read( monitor, path, result ) )
        {
            return result;
        }
        return null;

        static bool Read( IActivityMonitor monitor,
                          string path,
                          Dictionary<Uri, NormalizedPath> result )
        {
            bool success = true;
            foreach( var p in Directory.EnumerateDirectories( path, "*", SearchOption.TopDirectoryOnly ) )
            {
                var pGitConfig = p + "/.git/config";
                if( File.Exists( pGitConfig ) )
                {
                    var uri = ReadOriginUri( monitor, pGitConfig );
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
                    success &= Read( monitor, p, result );
                }
            }
            return success;

            static Uri? ReadOriginUri( IActivityMonitor monitor, string gitConfigPath )
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
