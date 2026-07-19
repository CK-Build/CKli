using CK.Core;
using CKli.Core;
using System;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    [Description( "Closes a Conformant SVersion branch if it exists." )]
    [CommandPath( "branch close" )]
    public bool BranchClose( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description( "Branch name to close." )]
                             string branchName,
                             [Description( "Do not integrate the closed branch in its parent and delete it. Leave it as-is." )]
                             bool discard )
    {
        // Not sure here...
        // Should the close only be "global"?
        // We can handle a mechanism here where the suppress the branch name only if it doesn't appear anywhere anymore in the World...
        // But any build of an upstream will recreate the hot branch in downstream repositories...
        // So this seems to be a rather useless complexity.
        // => Choosing the "global only" approach for the moment.
        if( context.CurrentDirectory != World.Name.WorldRoot )
        {
            // This explicits the "global" approach for the user.
            monitor.Error( $"""
                            Closing a branch has an impact on the whole World. This command must be run at the root of the World:
                            {World.Name.WorldRoot}
                            """ );
            return false;
        }

        var b = _namespace.FindRequired( monitor, branchName );
        if( b == null )
        {
            return false;
        }
        if( b.IsRoot )
        {
            monitor.Error( "The root branch cannot be closed." );
            return false;
        }
        if( !discard )
        {
            bool success = TryGetAllWithoutIssue( monitor, out var allInfo, "closing a branch" );
            if( success )
            {
                foreach( var info in allInfo )
                {
                    var hotBranch = info.Branches[b.Index];
                    if( hotBranch.Exists )
                    {
                        success &= hotBranch.Close( monitor );
                    }
                }
            }
            if( !success )
            {
                return false;
            }
        }
        var newNamespace = _namespace.Remove( b );
        return SaveBranchNamespace( monitor, newNamespace );
    }

}

