using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CKli.Core;

sealed class FileHelper
{
    public static bool TryMoveFolder( IActivityMonitor monitor,
                                      NormalizedPath from,
                                      NormalizedPath to,
                                      HashSet<NormalizedPath>? potentiallyEmptyFolders = null )
    {
        try
        {
            if( Directory.Exists( to ) )
            {
                // See https://stackoverflow.com/questions/1622597/renaming-directory-with-same-name-different-case
                if( from.Path.Equals( to.Path, StringComparison.OrdinalIgnoreCase ) )
                {
                    var tempName = to + "[__CASING]";
                    Directory.Move( from, tempName );
                    Directory.Move( tempName, to );
                    return true;
                }
                monitor.Error( $"The target folder '{to}' exists. Failed to move '{from}'." );
                return false;
            }
            var parent = to.RemoveLastPart();
            if( !Directory.Exists( parent ) ) Directory.CreateDirectory( parent );
            Directory.Move( from, to );
            potentiallyEmptyFolders?.Add( from.RemoveLastPart() );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While moving folder from '{from}' to '{to}'.", ex );
            return false;
        }
    }

    internal static bool DeleteFolder( IActivityMonitor monitor, string path )
    {
        if( !DeleteClonedFolderOnly( monitor, path, out bool isClonedFolder ) )
        {
            if( isClonedFolder ) return false;
            foreach( var d in Directory.EnumerateDirectories( path ) )
            {
                if( !DeleteFolder( monitor, d ) )
                {
                    return false;
                }
            }
            return DoDeleteFolder( monitor, path );
        }
        return true;
    }

    static bool DeleteClonedFolderOnly( IActivityMonitor monitor, string path, out bool isClonedFolder )
    {
        isClonedFolder = false;
        var gitObjectsPath = Path.Combine( path, ".git", "objects" );
        if( Directory.Exists( gitObjectsPath ) )
        {
            isClonedFolder = true;
            foreach( var hash in Directory.EnumerateDirectories( gitObjectsPath ) )
            {
                foreach( var gitObjectFile in Directory.EnumerateFiles( hash ) )
                {
                    File.SetAttributes( gitObjectFile, FileAttributes.Normal );
                }
            }
            return DoDeleteFolder( monitor, path );
        }
        return false;
    }

    static bool DoDeleteFolder( IActivityMonitor monitor, string path )
    {
        int tryCount = 0;
        for(; ; )
        {
            try
            {
                if( Directory.Exists( path ) ) Directory.Delete( path, true );
                return true;
            }
            catch( Exception ex )
            {
                if( ++tryCount > 5 ) return false;
                monitor.Warn( $"While trying to delete folder '{path}'.", ex );
                Thread.Sleep( 100 );
            }
        }
    }

}
