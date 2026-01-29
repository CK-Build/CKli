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
                "Deletes a repository on the hosting provider. Requires --force to confirm.",
                [("url", "Repository URL (HTTPS or SSH format).")],
                [],
                [
                    (["--force", "-f"], "Confirm deletion. Required to proceed.")
                ] )
    {
    }

    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                          CKliEnv context,
                                                                          CommandLineArguments cmdLine )
    {
        string url = cmdLine.EatArgument();
        bool force = cmdLine.EatFlag( "--force", "-f" );

        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }

        if( !force )
        {
            var screenType = context.Screen.ScreenType;
            context.Screen.Display( screenType.WarningMessage( "Deletion requires --force flag to confirm." )
                .AddBelow( screenType.Text( "This action is irreversible.", ConsoleColor.Yellow ).Box( marginLeft: 4 ) ) );
            return false;
        }

        var provider = await GitHostingProviderDetector.ResolveProviderAsync( monitor, context.SecretsStore, url );
        if( provider == null )
        {
            return false;
        }

        using( provider )
        {
            var parsed = provider.ParseRemoteUrl( url );
            if( parsed == null )
            {
                monitor.Error( $"Could not parse owner/repository from URL: {url}" );
                return false;
            }

            monitor.Warn( $"Deleting repository {parsed.Value.Owner}/{parsed.Value.RepoName} on {provider.HostName}..." );

            var result = await provider.DeleteRepositoryAsync( monitor, parsed.Value.Owner, parsed.Value.RepoName );
            if( !result.Success )
            {
                monitor.Error( result.ErrorMessage ?? "Failed to delete repository." );
                return false;
            }

            var screenType = context.Screen.ScreenType;
            context.Screen.Display( screenType.SuccessMessage( $"Repository deleted: {parsed.Value.Owner}/{parsed.Value.RepoName}" ) );
            return true;
        }
    }
}
