using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core;

sealed class FileHelper
{
    public static bool TryMoveFolder( IActivityMonitor monitor, NormalizedPath from, NormalizedPath to, int commonPathLength = 0 )
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
                }
                else
                {
                    monitor.Error( $"The target folder '{to.Path.AsSpan( commonPathLength )}' exists. Failed to move '{from.Path.AsSpan( commonPathLength )}'." );
                    return false;
                }
            }
            Directory.Move( from, to );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While moving folder from '{from.Path.AsSpan( commonPathLength )}' to '{to.Path.AsSpan( commonPathLength )}'.", ex );
            return false;
        }
    }

    internal static bool TryDeleteFolder( IActivityMonitor monitor, NormalizedPath path, int commonPathLength = 0 )
    {
        try
        {
            Directory.Delete( path );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Warn( $"While trying to delete folder '{path.Path.AsSpan( commonPathLength )}'.", ex );
            return false;
        }
    }
}
