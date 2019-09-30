using CK.Core;
using CK.Text;

namespace CK.Env.Plugin
{
    public class GlobalJsonFile : TextFilePluginBase, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;

        public GlobalJsonFile( GitFolder f, NormalizedPath branchPath, SolutionSpec solutionSpec )
             : base( f, branchPath, f.SubPath.AppendPart( "global.json" ) )
        {
            _solutionSpec = solutionSpec;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( nameof( GlobalJsonFile ) );

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            CreateOrUpdate( m, _solutionSpec.GlobalJsonSdkVersion == null ? null : $"{{ \"sdk\": {{ \"version\": \"{_solutionSpec.GlobalJsonSdkVersion}\" }} }}" ); 
        }
    }
}
