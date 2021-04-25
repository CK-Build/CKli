using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using CK.Core;

namespace CodeCake
{
    public partial class Build : CodeCakeHost
    {
        public Build()
        {
            Cake.Log.Verbosity = Verbosity.Diagnostic;

            StandardGlobalInfo globalInfo = CreateStandardGlobalInfo()
                                                .AddDotnet()
                                                .SetCIBuildTag();
            var m = new ActivityMonitor();
            globalInfo.TerminateIfShouldStop();
            globalInfo.GetDotnetSolution().Clean( m );

            globalInfo.GetDotnetSolution().Build( m );

            if( Cake.InteractiveMode() == InteractiveMode.NoInteraction
            || Cake.ReadInteractiveOption( "RunUnitTests", "Run Unit Tests?", 'Y', 'N' ) == 'Y' )
            {
                globalInfo.GetDotnetSolution().Test( m );
            }

            globalInfo.GetDotnetSolution().Pack();


            if( globalInfo.IsValid )
            {
                globalInfo.PushArtifacts();
            }
        }
    }
}
