using CK.Core;
using CKli.Core;
using System.IO;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Shell independent command implementations.
/// </summary>
public static class CKliCommands
{
    static CommandNamespace _commands;

    static CKliCommands()
    {
        var c = new CommandNamespaceBuilder();
        c.Add( new CKliClone() );
        c.Add( new CKliPull() );
        c.Add( new CKliFetch() );
        c.Add( new CKliPush() );
        c.Add( new CKliLayoutFix() );
        c.Add( new CKliLayoutXif() );
        c.Add( new CKliRepoAdd() );
        c.Add( new CKliRepoRemove() );
        c.Add( new CKliPlugin() );
        c.Add( new CKliPluginCreate() );
        c.Add( new CKliPluginRemove() );
        c.Add( new CKliPluginAdd() );
        c.Add( new CKliPluginEnable() );
        c.Add( new CKliPluginDisable() );
        _commands = c.Build();
    }

    /// <summary>
    /// Gets the intrinsic CKli commands.
    /// </summary>
    public static CommandNamespace Commands => _commands;

    public static ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                      CommandCommonContext context,
                                                      CommandLineArguments cmdLine )
    {
        var (stack, world) = StackRepository.TryOpenWorldFromPath( monitor, context, out bool error, skipPullStack: true );
        if( error )
        {
            Throw.DebugAssert( stack == null && world == null );
            return ValueTask.FromResult( false );
        }
        try
        {
            var commands = world == null || world.DefinitionFile.IsPluginsDisabled
                            ? _commands
                            : world.Commands;
            var cmd = commands.FindForExecution( monitor, cmdLine, out var helpPath );
            if( cmd == null )
            {
                HelpDisplay.Display( commands.GetForHelp( helpPath ) );
                return ValueTask.FromResult( false );
            }
            return cmd.HandleCommandAsync( monitor, context, cmdLine );
        }
        finally
        {
            stack?.Dispose();
        }
    }

}
