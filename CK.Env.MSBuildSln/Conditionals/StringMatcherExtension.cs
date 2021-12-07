using CK.Core;

namespace CK.Env.MSBuildSln
{
    static class StringMatcherExtension
    {
        public static bool TryMatchTo( this StringMatcher @this, char last )
        {
            int idx = @this.Text.IndexOf( last, @this.StartIndex, @this.Length );
            if( idx < 0 ) return false;
            @this.UncheckedMove( idx - @this.StartIndex + 1 );
            return true;
        }
    }
}
