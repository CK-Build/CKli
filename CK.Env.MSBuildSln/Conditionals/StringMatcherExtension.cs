using CK.Core;
using System;

namespace CK.Env.MSBuildSln
{
    static class StringMatcherExtension
    {
        public static bool TryMatchTo( this ref ReadOnlySpan<char> head, char last )
        {
            int idx = head.IndexOf( last );
            if( idx < 0 ) return false;
            head = head.Slice( idx + 1 );
            return true;
        }
    }
}
