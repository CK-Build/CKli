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
        c.Add( new CKliLog() );
        c.Add( new CKliClone() );
        c.Add( new CKliDotNet() );
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
    /// Helper that calls <see cref="HandleCommandAsync(IActivityMonitor, CKliEnv, CommandLineArguments)"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="cmdLine">The command line to handle.</param>
    /// <returns>True on success, false on error.</returns>
    public static ValueTask<bool> ExecAsync( IActivityMonitor monitor,
                                             CKliEnv context,
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
                                                      CKliEnv context,
                                                      CommandLineArguments cmdLine )
    {
        // First, tries to locate a CKli intrinsic command and handles it independently when found:
        // one cannot mix the 2 kind of commands: some CKli commands loads the current Stack and World if
        // needed, we cannot pre-load the current World here.
        // When TryFindForExecution returns false, the command exists but it misses one or more arguments.
        if( !_commands.TryFindForExecution( monitor, cmdLine, out var helpPath ) )
        {
            context.Screen.DisplayHelp( _commands.GetForHelp( helpPath, null ), cmdLine );
            return ValueTask.FromResult( false );
        }
        // If it's a CKli command, execute it or, if the --help has bee requested, display the help.
        if( cmdLine.FoundCommand != null )
        {
            if( cmdLine.HasHelp )
            {
                context.Screen.DisplayHelp( _commands.GetForHelp( helpPath, null ),
                                            cmdLine,
                                            CKliRootEnv.GlobalOptions?.Invoke() ?? default,
                                            CKliRootEnv.GlobalFlags?.Invoke() ?? default );
                return ValueTask.FromResult( true );
            }
            return ExecuteAsync( monitor, context, cmdLine );
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
            context.Screen.DisplayHelp( _commands.GetForHelp( helpPath, null ),
                                        cmdLine,
                                        CKliRootEnv.GlobalOptions?.Invoke() ?? default,
                                        CKliRootEnv.GlobalFlags?.Invoke() ?? default );
            return ValueTask.FromResult( false );
        }
        try
        {
            // We are in a World.
            if( !world.Commands.TryFindForExecution( monitor, cmdLine, out helpPath )
                || cmdLine.FoundCommand == null )
            {
                // No luck.
                // Displays the help in the context of the World. The World's commands
                // contain the CKli command: the help mixes the 2 kind of commands if needed.
                context.Screen.DisplayHelp( world.Commands.GetForHelp( helpPath, _commands ),
                                            cmdLine,
                                            CKliRootEnv.GlobalOptions?.Invoke() ?? default,
                                            CKliRootEnv.GlobalFlags?.Invoke() ?? default );
                return ValueTask.FromResult( false );
            }
            // We have a plugin command.
            Throw.DebugAssert( "This cannot be a CKli command: we'd have located it initially.", cmdLine.FoundCommand.PluginTypeInfo != null );
            if( cmdLine.HasHelp )
            {
                // If the --help has bee requested, displys the help.
                // No need to lookup the CKli command here: we have a plugin command.
                context.Screen.DisplayHelp( world.Commands.GetForHelp( helpPath, null ),
                                            cmdLine,
                                            CKliRootEnv.GlobalOptions?.Invoke() ?? default,
                                            CKliRootEnv.GlobalFlags?.Invoke() ?? default );
                return ValueTask.FromResult( true );
            }
            if( cmdLine.FoundCommand.IsDisabled )
            {
                monitor.Error( $"Command '{cmdLine.FoundCommand.CommandPath}' exists but its type '{cmdLine.FoundCommand.PluginTypeInfo.TypeName}' is disabled in plugin '{cmdLine.FoundCommand.PluginTypeInfo.Plugin.FullPluginName}'." );
                return ValueTask.FromResult( false );
            }
            return ExecuteAsync( monitor, context, cmdLine );
        }
        finally
        {
            stack?.Dispose();
        }
    }

    static async ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
    {
        Throw.DebugAssert( cmdLine.FoundCommand != null );
        var success = await cmdLine.FoundCommand.HandleCommandAsync( monitor, context, cmdLine );
        if( success && !cmdLine.IsClosed )
        {
            monitor.Error( $"""
                The command '{cmdLine.FoundCommand.CommandPath}' implementation in '{cmdLine.FoundCommand.PluginTypeInfo?.TypeName ?? "CKli"}' is buggy.
                The command line MUST be closed before executing the command.
                """ );
            return false;
        }
        if( cmdLine.IsClosed && cmdLine.RemainingCount > 0 )
        {
            if( success )
            {
                monitor.Error( $"""
                    The command '{cmdLine.FoundCommand.CommandPath}' implementation in '{cmdLine.FoundCommand.PluginTypeInfo?.TypeName ?? "CKli"}' is buggy.
                    Arguments remains in the command line but the command handler returned true.
                    """ );
            }
            context.Screen.DisplayHelp( [new CommandHelp( cmdLine.FoundCommand )], cmdLine, default, default );
        }
        return success;
    }
}
