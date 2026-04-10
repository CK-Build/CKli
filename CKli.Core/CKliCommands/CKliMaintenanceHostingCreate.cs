using CK.Core;
using System;
using System.Threading.Tasks;

namespace CKli.Core;


// Ambiguous...
//
// "ckli stack create https://github.com/signature-opensource/Chameleon-Stack" --private
// => Creates a Stack:
//   - The remote repository is created...
//   - ...checked out in the in the current directory (local folder "Chameleon/").
//   - Default stack files are created.

// "ckli repo create https://github.com/signature-opensource/XN211-Server" --allow-lts
// => Creates a Repo:
//   - We must be in a World.
//   - The remote repository is created...
//   - ...checked out in the in the current directory (local folder "XN211-Server/").
//   - An event should be raised: plugins must be able to initialize it.
//

///// <summary>
///// Creates a new repository on the hosting provider.
///// 
///// </summary>
//sealed class CKliMaintenanceHostingCreate : Command
//{
//    internal CKliMaintenanceHostingCreate()
//        : base( null,
//                "maintenance hosting create",
//                "Creates a new repository on the hosting provider.",
//                [("url", "Repository url (https:// or file://).")],
//                [],
//                [
//                    (["--private"], "Make the repository private. By default, the access is the same as the current stack."),
//                    (["--public"], "Make the repository public. By default, the access is the same as the current stack.")
//                ] )
//    {
//    }

//    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
//                                                                          CKliEnv context,
//                                                                          CommandLineArguments cmdLine )
//    {
//        Uri? url = ReadRepoUrlArgument( monitor, cmdLine );
//        if( url == null )
//        {
//            return false;
//        }

//        bool isPrivate = cmdLine.EatFlag( "--private" );
//        bool isPublic = cmdLine.EatFlag( "--public" );
//        if( isPrivate && isPublic )
//        {
//            monitor.Error( "Cannot specify both --private and --public." );
//            return false;
//        }
//        if( !cmdLine.Close( monitor ) )
//        {
//            return false;
//        }
//        // If --private nor --public are specified, lookup the current stack access.
//        // Note that this is the nominal case: this creates, by default, a repository with the same access
//        // as the current stack.
//        if( !isPrivate && !isPublic )
//        {
//            var currentStack = StackRepository.TryOpenFromPath( monitor, context, out bool error, skipPullStack: true );
//            if( currentStack == null )
//            {
//                if( !error )
//                {
//                    monitor.Error( "The current directory is not in a Stack directory: --public or --private must be specified." );
//                }
//                return false;
//            }
//            isPublic = currentStack.IsPublic;
//            isPrivate = !isPublic;
//            currentStack.Dispose();
//        }
//        var gitKey = new GitRepositoryKey( context.SecretsStore, url, isPublic );
//        if( !gitKey.TryGetHostingInfo( monitor, out var hostingProvider, out var repoPath ) )
//        {
//            return false;
//        }
//        HostedRepositoryInfo? info = await hostingProvider.CreateRepositoryAsync( monitor, repoPath, isPrivate ).ConfigureAwait( false );
//        if( info == null )
//        {
//            return false;
//        }

//        var screenType = context.Screen.ScreenType;
//        var display = screenType.SuccessMessage( $"Repository '{info.RepoPath}' created in '{hostingProvider.BaseUrl}'." )
//            .AddBelow( screenType.UrlField( "Url", info.WebUrl ).Box( marginLeft: 4 ) );
//        context.Screen.Display( display );

//        return true;
//    }

//    internal static Uri? ReadRepoUrlArgument( IActivityMonitor monitor, CommandLineArguments cmdLine )
//    {
//        string sUrl = cmdLine.EatArgument();
//        var errorMessage = Uri.TryCreate( sUrl, UriKind.Absolute, out var url )
//                                ? GitRepositoryKey.GetRepositoryUrlError( url )
//                                : $"Unable to parse url: '{sUrl}'.";
//        if( errorMessage != null )
//        {
//            monitor.Error( errorMessage );
//            return null;
//        }

//        return url;
//    }
//}
