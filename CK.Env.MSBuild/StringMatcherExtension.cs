using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    static class StringMatcherExtension
    {
        static public bool TryMatchTo( this StringMatcher @this, char last )
        {
            int idx = @this.Text.IndexOf( last, @this.StartIndex, @this.Length );
            if( idx < 0 ) return false;
            @this.UncheckedMove( idx - @this.StartIndex + 1 );
            return true;
        }
    }
}
