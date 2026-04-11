using CK.Core;
using CKli.Core;
using System;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliRepoCreate : Command
{
    public CKliRepoCreate()
        : base( null,
                "repo create",
                "Creates a new remote repository and adds it to the current world. The repository is cloned in the current directory.",
                [("repositoryUrl", "Url of the repository to create and add to the current World.")],
                [],
                [
                    (["--allow-lts"], "Allows the current world to be a Long Term Support world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        return CKliRepoAdd.RepositoryAddOrCreateAsync( monitor, this, context, cmdLine, create: true );
    }

}
