using CSemVer;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuild
{
    public class SVersionRange
    {
        static public SVersion TryParseSimpleRange( string r )
        {
            SVersion v = SVersion.TryParse( r );
            if( !v.IsValid && r != null && r.Length > 3 && r[0] == '[' && r[r.Length-1] == ']' )
            {
                v = SVersion.TryParse( r.Substring( 1, r.Length - 2 ) );
            }
            return v;
        }

    }
}
