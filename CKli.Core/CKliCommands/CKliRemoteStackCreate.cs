using CK.Core;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Stack create command - creates a new, non-existing stack locally.
/// </summary>
sealed class CKliRemoteStackCreate : Command
{
    internal CKliRemoteStackCreate()
        : base( null,
                "remote stack create",
                "Creates a new Stack in the current directory.",
                [("stackName", "The name of the stack to create (without '-Stack' suffix).")],
                [(["--url", "-u"], "Optional remote URL. Sets up origin but does not push.", false)],
                [
                    (["--private"], "Use '.PrivateStack' folder instead of '.PublicStack'."),
                ] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string stackName = cmdLine.EatArgument();
        if( string.IsNullOrWhiteSpace( stackName ) )
        {
            monitor.Error( "Missing required <stackName> argument." );
            return ValueTask.FromResult( false );
        }

        Uri? url = null;
        string? sUrl = cmdLine.EatSingleOption( "--url", "-u" );
        if( sUrl != null )
        {
            if( !Uri.TryCreate( sUrl, UriKind.Absolute, out url ) )
            {
                monitor.Error( $"Invalid --url option '{sUrl}'. It must be an absolute URL." );
                return ValueTask.FromResult( false );
            }
        }

        bool isPrivate = cmdLine.EatFlag( "--private" );
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }

        using( var stack = StackRepository.Create( monitor, context, stackName, !isPrivate, url ) )
        {
            return ValueTask.FromResult( stack != null );
        }
    }
}
