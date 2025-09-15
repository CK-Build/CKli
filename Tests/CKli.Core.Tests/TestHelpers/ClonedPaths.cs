using CK.Core;
using System.IO;
using System.Runtime.CompilerServices;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

static class ClonedPaths
{
    static NormalizedPath _clonedPath = TestHelper.TestProjectFolder.AppendPart( "Cloned" );

    public static NormalizedPath EnsureCleanFolder( [CallerMemberName] string? name = null )
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
        return path;
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

}
