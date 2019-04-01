using System;
using System.Collections.Generic;
using System.IO;

namespace CK.IniFile.SpecificImplementation
{
    public class NpmrcFormat : IniFormat<NpmrcLine>
    {
        public static NpmrcFormat DefaultConfig => new NpmrcFormat();
        public NpmrcFormat()
        {
            CommentChar = new List<char>( ";#" );
            KeyDelimiter = '=';
            Duplication = IniDuplication.Ignored;
        }
        internal override NpmrcLine ParseLine( string line )
        {
            IniLine iniLine = InternalParse( line );
            string[] split = iniLine.Key.Split( ':' );
            if( split.Length == 1 )
            {
                return new NpmrcLine( iniLine );
            }
            if( split.Length == 2 )
            {
                if( split[0].StartsWith( "//" ) )
                {
                    return new NpmrcLine( iniLine, new Uri( "https:" + split[0] ) );
                }
                return new NpmrcLine( iniLine, split[0] );
            }
            else
            {
                throw new InvalidDataException( "Invalid .npmrc line: the key should contain only one ':'" );
            }

        }
    }
}
