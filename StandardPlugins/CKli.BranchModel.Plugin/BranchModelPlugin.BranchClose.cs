using CK.Core;
using CKli.Core;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    [Description( "Closes or discards a Conformant SVersion branch it it exists." )]
    [CommandPath( "branch close" )]
    public bool BranchClose( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description( "Branch name to close." )]
                             string branchName,
                             [Description( "Do not integrate the closing branch in its parent." )]
                             bool discard )
    {
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null ) return false;
        return true;
    }

}

