using CK.Core;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Stack set-remote-url command - changes the stack's remote URL (origin).
/// </summary>
sealed class CKliRemoteStackMigrate : Command
{
    internal CKliRemoteStackMigrate()
        : base( null,
                "remote stack migrate",
                "Changes the stack's remote URL (origin).",
                [("newUrl", "The new remote URL for the stack.")],
                [],
                [(["--no-push"], "Do not push after changing the URL.")] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string? newUrlString = cmdLine.EatArgument();
        if( string.IsNullOrWhiteSpace( newUrlString ) )
        {
            monitor.Error( "Missing required <newUrl> argument." );
            return ValueTask.FromResult( false );
        }

        bool noPush = cmdLine.EatFlag( "--no-push" );
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }

        // Validate the URL
        var newUrl = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( monitor, new Uri( newUrlString, UriKind.RelativeOrAbsolute ), out _ );
        if( newUrl == null )
        {
            return ValueTask.FromResult( false );
        }

        var stack = StackRepository.TryOpenFromPath( monitor, context, out bool error, skipPullStack: true );
        if( error ) return ValueTask.FromResult( false );
        if( stack == null )
        {
            monitor.Error( "Not in a Stack directory." );
            return ValueTask.FromResult( false );
        }

        try
        {
            return ValueTask.FromResult( stack.SetRemoteUrl( monitor, newUrl, push: !noPush )
                                         && stack.Close( monitor ) );
        }
        finally
        {
            stack.Dispose();
        }
    }
}
