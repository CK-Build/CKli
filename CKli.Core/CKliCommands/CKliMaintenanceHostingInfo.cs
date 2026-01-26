using CK.Core;
using CKli.Core.GitHosting;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Gets repository information from the hosting provider.
/// </summary>
sealed class CKliMaintenanceHostingInfo : Command
{
    internal CKliMaintenanceHostingInfo()
        : base( null,
                "maintenance hosting info",
                "Gets repository information from the hosting provider.",
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

            var result = await provider.GetRepositoryInfoAsync( monitor, parsed.Value.Owner, parsed.Value.RepoName );
            if( !result.Success )
            {
                monitor.Error( result.ErrorMessage ?? "Failed to get repository info." );
                return false;
            }

            var info = result.Data!;
            var screenType = context.Screen.ScreenType;

            var display = screenType.Unit
                .AddBelow( screenType.Text( $"Repository: {info.Owner}/{info.Name}" ) )
                .AddBelow( screenType.Text( $"Description: {info.Description ?? "(none)"}" ) )
                .AddBelow( screenType.Text( $"Private: {info.IsPrivate}" ) )
                .AddBelow( screenType.Text( $"Archived: {info.IsArchived}" ) )
                .AddBelow( screenType.Text( $"Empty: {info.IsEmpty}" ) )
                .AddBelow( screenType.Text( $"Default branch: {info.DefaultBranch ?? "(none)"}" ) )
                .AddBelow( screenType.Text( $"HTTPS URL: {info.CloneUrlHttps ?? "(none)"}" ) )
                .AddBelow( screenType.Text( $"SSH URL: {info.CloneUrlSsh ?? "(none)"}" ) )
                .AddBelow( screenType.Text( $"Web URL: {info.WebUrl ?? "(none)"}" ) )
                .AddBelow( screenType.Text( $"Created: {info.CreatedAt?.ToString( "yyyy-MM-dd HH:mm:ss" ) ?? "(unknown)"}" ) )
                .AddBelow( screenType.Text( $"Updated: {info.UpdatedAt?.ToString( "yyyy-MM-dd HH:mm:ss" ) ?? "(unknown)"}" ) );

            context.Screen.Display( display );
            return true;
        }
    }
}
