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
                [
                    (["--allow-multi-repo"], """
                                            Proceed even if the current path is above multiple Repos.
                                            By default, the current path must be in a Repo.
                                            """)
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        return ValueTask.FromResult( CKliTagPull.PullOrPushTags( monitor, this, context, cmdLine, pull: false ) );
    }
}
