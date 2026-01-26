using CK.Core;
using CKli.Core.GitHosting;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Archives a repository on the hosting provider.
/// </summary>
sealed class CKliMaintenanceHostingArchive : Command
{
    internal CKliMaintenanceHostingArchive()
        : base( null,
                "maintenance hosting archive",
                "Archives a repository on the hosting provider.",
                [("url", "Repository URL (HTTPS or SSH format).")],
                [],
                [] )
    {
    }

    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                          CKliEnv context,
                                                                          CommandLineArguments cmdLine )
    {
        string url = cmdLine.EatArgument();
        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }

        var provider = await ProviderDetector.ResolveProviderAsync( monitor, context.SecretsStore, url );
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

            monitor.Info( $"Archiving repository {parsed.Value.Owner}/{parsed.Value.RepoName} on {provider.InstanceId}..." );

            var result = await provider.ArchiveRepositoryAsync( monitor, parsed.Value.Owner, parsed.Value.RepoName );
            if( !result.Success )
            {
                monitor.Error( result.ErrorMessage ?? "Failed to archive repository." );
                return false;
            }

            monitor.Info( $"Repository {parsed.Value.Owner}/{parsed.Value.RepoName} has been archived." );
            return true;
        }
    }
}
