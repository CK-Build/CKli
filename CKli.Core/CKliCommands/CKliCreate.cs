using CK.Core;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Create (stack) command.
/// </summary>
sealed class CKliCreate : Command
{
    internal CKliCreate()
        : base( null,
                "create",
                "Creates a new Stack and its remote repository in the current directory.",
                [("stackUrl", "The url stack repository to create. The repository name must end with '-Stack'.")],
                [],
                [
                    (["--private"], "Specify a private Stack. By default, a Stack is public."),
                    (["--ignore-parent-stack"], "Allows the new Stack to be inside an existing one.")
       ] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                        CKliEnv context,
                                                                        CommandLineArguments cmdLine )
    {
        string sUrl = cmdLine.EatArgument();
        if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var uri ) )
        {
            monitor.Error( $"Invalid <stackUrl> argument '{sUrl}'. It must be an absolute url." );
            return false;
        }
        bool isPrivate = cmdLine.EatFlag( "--private" );
        bool ignoreParentStack = cmdLine.EatFlag( "--ignore-parent-stack" );
        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }
        using( var stack = await StackRepository.CreateAsync( monitor, context, uri, !isPrivate, ignoreParentStack ) )
        {
            return stack != null;
        }
    }
}
