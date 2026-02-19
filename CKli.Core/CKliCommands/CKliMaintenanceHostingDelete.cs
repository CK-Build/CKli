using CK.Core;
using CKli.Core.GitHosting;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Deletes a repository on the hosting provider.
/// </summary>
sealed class CKliMaintenanceHostingDelete : Command
{
    internal CKliMaintenanceHostingDelete()
        : base( null,
                "maintenance hosting delete",
                "Deletes a repository on the hosting provider.",
                [("url", "Repository url (https:// or file://).")],
                [],
                [
                    (["--confirm"], "Confirm deletion. Required to proceed.")
                ] )
    {
    }

    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                          CKliEnv context,
                                                                          CommandLineArguments cmdLine )
    {
        Uri? url = CKliMaintenanceHostingCreate.ReadRepoUrlArgument( monitor, cmdLine );
        if( url == null )
        {
            return false;
        }
        bool confirm = cmdLine.EatFlag( "--confirm" );

        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }

        var gitKey = new GitRepositoryKey( context.SecretsStore, url, isPublic: false );
        if( !gitKey.TryGetHostingInfo( monitor, out var hostingProvider, out var repoPath ) )
        {
            return false;
        }
        if( !confirm )
        {
            monitor.Error( "Deletion requires --confirm flag (this action is irreversible)." );
            return false;
        }
        if( await hostingProvider.DeleteRepositoryAsync( monitor, repoPath ).ConfigureAwait( false ) )
        {
            monitor.Info( ScreenType.CKliScreenTag, $"Repository '{url}' deleted successfully." );
            return true;
        }
        return false;
    }
}
