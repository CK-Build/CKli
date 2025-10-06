using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Helper that calls <see cref="HandleCommandAsync(IActivityMonitor, CommandCommonContext, CommandLineArguments)"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="cmdLine">The command line to handle.</param>
    /// <returns>True on success, false on error.</returns>
    public static ValueTask<bool> ExecAsync( IActivityMonitor monitor,
                                             CommandCommonContext context,
                                             params IEnumerable<object> cmdLine )
    {
        return HandleCommandAsync( monitor, context, new CommandLineArguments( cmdLine.Select( o => o.ToString() ).ToArray()! ) );
    }

    /// <summary>
    /// Central command line handler.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="cmdLine">The command line to handle.</param>
    /// <returns>True on success, false on error.</returns>
    public static ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                      CommandCommonContext context,
                                                      CommandLineArguments cmdLine )
    {
        // First, tries to locate a CKli intrinsic command and handles it independently when found:
        // one cannot mix the 2 kind of commands: some CKli commands loads the current Stack and World if
        // needed, we cannot pre-load the current World here.
        // When TryFindForExecution returns false, the command exists but it misses one or more arguments.
        if( !_commands.TryFindForExecution( monitor, cmdLine, out var cmd, out var helpPath ) )
        {
            HelpDisplay.Display( _commands.GetForHelp( helpPath ) );
            return ValueTask.FromResult( false );
        }
        // If it's a CKli command, execute it.
        if( cmd != null )
        {
            return cmd.HandleCommandAsync( monitor, context, cmdLine );
        }
        // Not a CKli command. Opens the current World and tries to find a plugin command.
        var (stack, world) = StackRepository.TryOpenWorldFromPath( monitor, context, out bool error, skipPullStack: true );
        if( error )
        {
            Throw.DebugAssert( stack == null && world == null );
            return ValueTask.FromResult( false );
        }
        // No current World (not in a Stack directory): we can only display help on the CKli commands.
        if( world == null )
        {
            HelpDisplay.Display( _commands.GetForHelp( helpPath ) );
            return ValueTask.FromResult( false );
        }
        try
        {
            // We are in a World.
            if( !world.Commands.TryFindForExecution( monitor, cmdLine, out cmd, out helpPath )
                || cmd == null )
            {
                // No luck.
                // Displays the help in the context of the World. The World's commands
                // contain the CKli command: the help mixes the 2 kind of commands if needed.
                HelpDisplay.Display( world.Commands.GetForHelp( helpPath ) );
                return ValueTask.FromResult( false );
            }
            // We have a plugin command.
            Throw.DebugAssert( "This cannot be a CKli command: we'd have located it initially.", cmd.PluginTypeInfo != null );
            if( cmd.IsDisabled )
            {
                monitor.Error( $"Command '{cmd.CommandPath}' exists but its type '{cmd.PluginTypeInfo.TypeName}' is disabled in plugin '{cmd.PluginTypeInfo.Plugin.FullPluginName}'." );
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
