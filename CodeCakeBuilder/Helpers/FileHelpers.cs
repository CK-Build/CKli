using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeCakeBuilder.Helpers
{
    public static class FileHelpers
    {
        public static string? FindSiblingDirectoryAbove( string start, string directoryName )
        {
            if( start == null ) throw new ArgumentNullException( "start" );
            if( directoryName == null ) throw new ArgumentNullException( "directortyName" );
            string? p = Path.GetDirectoryName( start );
            if( string.IsNullOrEmpty( p ) ) return null;
            string pF;
            while( !Directory.Exists( pF = Path.Combine( p, directoryName ) ) )
            {
                p = Path.GetDirectoryName( p );
                if( string.IsNullOrEmpty( p ) ) return null;
            }
            return pF;
        }
    }
}
