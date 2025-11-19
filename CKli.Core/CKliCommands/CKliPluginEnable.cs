using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPluginEnable : Command
{
    public CKliPluginEnable()
        : base( null,
                "plugin enable",
                """Enables a plugin or all of them if <name> is "global".""",
                [("name", """Plugin name to enable or "global".""")],
                [], [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        var name = cmdLine.EatArgument();
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && EnableOrDisablePlugin( monitor, context, name, enable: true ) ); 
    }

    internal static bool EnableOrDisablePlugin( IActivityMonitor monitor, CKliEnv context, string name, bool enable )
    {
        if( StackRepository.OpenFromPath( monitor, context, out var stack, skipPullStack: true ) )
        {
            try
            {
                var definitionFile = stack.GetWorldNameFromPath( monitor, context.CurrentDirectory )?.LoadDefinitionFile( monitor );
                if( definitionFile != null )
                {
                    // EnablePlugin handles the WorldDefinition file save and commit.
                    return definitionFile.EnablePlugin( monitor, name, enable );
                }
            }
            finally
            {
                stack.Dispose();
            }
        }
        return false;
    }

}
