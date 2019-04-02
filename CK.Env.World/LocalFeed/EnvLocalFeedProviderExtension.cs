using CK.Text;

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


        public static NormalizedPath GetZeroVersionCodeCakeBuilderExecutablePath( this IEnvLocalFeedProvider local, string solutionName )
        {
            return local.ZeroBuild.PhysicalPath.AppendPart( "Builders" ).AppendPart( solutionName ).AppendPart( "CodeCakeBuilder.dll" );
        }
    }
}
