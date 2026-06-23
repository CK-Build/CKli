using CK.Core;
using CKli.Core;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    [Description( """
        Opens a Conformant SVersion branch it it doesn't exist already.
        - For prerelease branches ('alpha', 'bravo', 'charlie', ...'zulu'), the parent branch is based on the lexicographic order.
        - For exploratory branches ('explo/name'), the parent branch is the currently checked out branch. 
        """ )]
    [CommandPath( "branch open" )]
    public bool BranchOpen( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Branch name to open." )]
                            string branchName )
    {
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null ) return false;
        return true;
    }

}

