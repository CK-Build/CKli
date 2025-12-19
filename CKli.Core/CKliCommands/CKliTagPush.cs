using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliTagPush : Command
{
    public CKliTagPush()
        : base( null,
                "tag push",
                """
                Pushes the specified tags from the current Repo into its remote "origin".
                Modifications of the remote tags are lost: the local replace them. 
                """,
                [("tag names", "One or more tag names to push.")],
                [],
                [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        return ValueTask.FromResult( CKliTagPull.PullOrPushTags( monitor, this, context, cmdLine, pull: false ) );
    }
}
