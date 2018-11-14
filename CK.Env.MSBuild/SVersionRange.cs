using CSemVer;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuild
{
    class SVersionRange
    {
        static public (bool Locked, SVersion Version) TryParseSimpleRange( string r )
        {
            SVersion v = SVersion.TryParse( r );
            if( !v.IsValid && r != null && r.Length > 3 && r[0] == '[' && r[r.Length - 1] == ']' )
            {
                return (true, SVersion.TryParse( r.Substring( 1, r.Length - 2 ) ));
            }
            return (false, v);
        }

    }
}
