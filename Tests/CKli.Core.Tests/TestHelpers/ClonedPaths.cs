using CK.Core;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

static class ClonedPaths
{
    static NormalizedPath _clonedPath = TestHelper.TestProjectFolder.AppendPart( "Cloned" );

    public static CommandCommonContext EnsureCleanFolder( [CallerMemberName] string? name = null, bool clearStackRegistryFile = true )
    {
        var path = _clonedPath.AppendPart( name );
        if( Directory.Exists( path ) )
        {
            foreach( var d in Directory.EnumerateDirectories( path ) )
            {
                DeleteFolder( d );
            }
        }
        else
        {
            Directory.CreateDirectory( path );
        }
        if( clearStackRegistryFile )
        {
            Throw.CheckState( StackRepository.ClearRegistry( TestHelper.Monitor ) );
        }
        return new CommandCommonContext( path );
    }

    public static void DeleteFolder( string path )
    {
        foreach( var d in Directory.EnumerateDirectories( path ) )
        {
            if( !DeleteClonedFolderOnly( d ) )
            {
                DeleteFolder( d );
            }
        }
        TestHelper.CleanupFolder( path, ensureFolderAvailable: false );
    }

    public static bool DeleteClonedFolderOnly( string d )
    {
        var gitObjectsPath = Path.Combine( d, ".git", "objects" );
        if( Directory.Exists( gitObjectsPath ) )
        {
            foreach( var hash in Directory.EnumerateDirectories( gitObjectsPath ) )
            {
                foreach( var gitObjectFile in Directory.EnumerateFiles( hash ) )
                {
                    File.SetAttributes( gitObjectFile, FileAttributes.Normal );
                }
            }
            TestHelper.CleanupFolder( d, ensureFolderAvailable: false );
            return true;
        }
        return false;
    }

    public static void MoveFolder( NormalizedPath from, NormalizedPath to )
    {
        int tryCount = 0;
        for(; ; )
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
                        return;
                    }
                    throw new CKException( $"The target folder '{to}' exists. Failed to move '{from}'." );
                }
                var parent = to.RemoveLastPart();
                if( !Directory.Exists( parent ) ) Directory.CreateDirectory( parent );
                Directory.Move( from, to );
                return;
            }
            catch( Exception ex ) when( ex is not CKException ) 
            {
                if( ++tryCount > 5 )
                {
                    throw new CKException( $"While moving folder from '{from}' to '{to}'.", ex );
                }
                Thread.Sleep( 100 );
            }
        }
    }

}
