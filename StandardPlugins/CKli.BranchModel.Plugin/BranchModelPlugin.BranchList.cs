using CK.Core;
using CKli.Core;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    [Description( "Lists opened Conformant SVersion branches." )]
    [CommandPath( "branch list" )]
    public bool BranchList( IActivityMonitor monitor,
                             CKliEnv context,
                              [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                              bool all = false )
    {
        var repos = all
                    ? World.GetAllDefinedRepo( monitor )
                    : World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( repos == null ) return false;
        return true;
    }

}

