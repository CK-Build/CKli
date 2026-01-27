using CK.Core;
using CKli.Core.GitHosting;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Creates a new repository on the hosting provider.
/// </summary>
sealed class CKliMaintenanceHostingCreate : Command
{
    internal CKliMaintenanceHostingCreate()
        : base( null,
                "maintenance hosting create",
                "Creates a new repository on the hosting provider.",
                [("url", "Repository URL (HTTPS or SSH format).")],
                [
                    (["--description", "-d"], "Repository description.", false)
                ],
                [
                    (["--private", "-p"], "Make the repository private (default: public)."),
                    (["--init"], "Initialize with a README file.")
                ] )
    {
    }

    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                          CKliEnv context,
                                                                          CommandLineArguments cmdLine )
    {
        string url = cmdLine.EatArgument();
        string? description = cmdLine.EatSingleOption( "--description", "-d" );
        bool isPrivate = cmdLine.EatFlag( "--private", "-p" );
        bool autoInit = cmdLine.EatFlag( "--init" );

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

            // Generate default description if not provided
            if( description == null )
            {
                string? stackName = null;
                var stack = StackRepository.TryOpenFromPath( monitor, context, out _, skipPullStack: true );
                if( stack != null )
                {
                    stackName = stack.StackName;
                    stack.Dispose();
                }
                description = RepositoryCreateOptions.GenerateDefaultDescription( parsed.Value.RepoName, stackName );
            }

            var options = new RepositoryCreateOptions
            {
                Owner = parsed.Value.Owner,
                Name = parsed.Value.RepoName,
                Description = description,
                IsPrivate = isPrivate,
                AutoInit = autoInit
            };

            monitor.Info( $"Creating repository {parsed.Value.Owner}/{parsed.Value.RepoName} on {provider.InstanceId}..." );

            var result = await provider.CreateRepositoryAsync( monitor, options );
            if( !result.Success )
            {
                monitor.Error( result.ErrorMessage ?? "Failed to create repository." );
                return false;
            }

            var info = result.Data!;
            var screenType = context.Screen.ScreenType;

            var display = screenType.SuccessMessage( $"Repository created: {info.Owner}/{info.Name}" )
                .AddBelow( screenType.UrlField( "HTTPS URL", info.CloneUrlHttps ).Box( marginLeft: 4 ) )
                .AddBelow( screenType.LabeledField( "SSH URL", info.CloneUrlSsh ).Box( marginLeft: 4 ) )
                .AddBelow( screenType.UrlField( "Web URL", info.WebUrl ).Box( marginLeft: 4 ) );

            context.Screen.Display( display );
            return true;
        }
    }
}
