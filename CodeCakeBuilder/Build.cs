using CK.Core;
using CodeCakeBuilder.Helpers;
using System.Threading.Tasks;

namespace CodeCake
{
    public partial class Build
    {
        readonly CCBOptions? _cCBOptions;

        public StandardGlobalInfo GlobalInfo { get; }
        public Build( IActivityMonitor m, CCBOptions cCBOptions, string? solutionDirectory )
        {
            GlobalInfo = CreateStandardGlobalInfo( m, solutionDirectory, cCBOptions );
            _cCBOptions = cCBOptions;
        }

        public async Task<bool> RunAsync( IActivityMonitor m )
        {
            await GlobalInfo.AddDotnet( m );
            GlobalInfo.SetCIBuildTag( m );
            if( GlobalInfo.GetShouldStop( m ) ) return true;
            if( !await GlobalInfo.GetDotnetSolution().Clean( m ) ) return false;
            if( !await GlobalInfo.GetDotnetSolution().Build( m ) ) return false;


            if( GlobalInfo.InteractiveMode == InteractiveMode.NoInteraction
            || GlobalInfo.ReadInteractiveOption( m, "RunUnitTests", "Run Unit Tests?", 'Y', 'N' ) == 'Y' )
            {
                await GlobalInfo.GetDotnetSolution().Test( m );
            }

            if( !await GlobalInfo.GetDotnetSolution().Pack( m ) ) return false;


            if( GlobalInfo.IsValid )
            {
                if( !await GlobalInfo.PushArtifacts( m ) ) return false;
            }
            return true;
        }
    }
}
