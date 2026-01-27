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
                .AddBelow( screenType.LabeledField( "Repository", $"{info.Owner}/{info.Name}" ) )
                .AddBelow( screenType.LabeledField( "Description", info.Description ) )
                .AddBelow( screenType.BooleanField( "Private", info.IsPrivate ) )
                .AddBelow( screenType.BooleanField( "Archived", info.IsArchived ) )
                .AddBelow( screenType.BooleanField( "Empty", info.IsEmpty ) )
                .AddBelow( screenType.LabeledField( "Default Branch", info.DefaultBranch ) )
                .AddBelow( screenType.UrlField( "HTTPS URL", info.CloneUrlHttps ) )
                .AddBelow( screenType.LabeledField( "SSH URL", info.CloneUrlSsh ) )
                .AddBelow( screenType.UrlField( "Web URL", info.WebUrl ) )
                .AddBelow( screenType.DateField( "Created", info.CreatedAt ) )
                .AddBelow( screenType.DateField( "Updated", info.UpdatedAt ) );

            context.Screen.Display( display );
            return true;
        }
    }
}
