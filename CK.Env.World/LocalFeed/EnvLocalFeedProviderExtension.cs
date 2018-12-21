using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public static class EnvLocalFeedProviderExtension
    {
        public static IEnvLocalFeed GetFeed( this IEnvLocalFeedProvider @this, BuildResultType type )
        {
            switch( type )
            {
                case BuildResultType.Local: return @this.Local;
                case BuildResultType.CI: return @this.CI;
                case BuildResultType.Release: return @this.Release;
            }
            return null;
        }
    }
}
