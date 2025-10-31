using CK.Core;
using CKli.Core;
using System;
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
        c.Add( new CKliIssue() );
        c.Add( new CKliExec() );
        c.Add( new CKliPull() );
        c.Add( new CKliFetch() );
        c.Add( new CKliPush() );
        c.Add( new CKliLayoutFix() );
        c.Add( new CKliLayoutXif() );
        c.Add( new CKliRepo() );
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
        // When TryFindForExecution returns false, the command exists but it misses one or more arguments
        // and an error has been emitted: display the help.
        // If it's a CKli command (cmdLine.FoundCommand != null) and the --help has been requested,
        // also display the help.

        if( !_commands.TryFindForExecution( monitor, cmdLine, out var helpPath )
            || (cmdLine.FoundCommand != null && cmdLine.HasHelp) )
        {
            context.Screen.DisplayHelp( _commands.GetForHelp( context.Screen.ScreenType, helpPath, null ),
                                        cmdLine,
                                        CKliRootEnv.GlobalOptions?.Invoke() ?? default,
                                        CKliRootEnv.GlobalFlags?.Invoke() ?? default );
            return ValueTask.FromResult( true );
        }
        // If it's a CKli command, we can now execute it.
        if( cmdLine.FoundCommand != null )
        {
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
            if( cmdLine.FoundCommand == null && cmdLine.ExpectCommand )
            {
                monitor.Error( $"Unknown command '{cmdLine.InitialAsStringArguments}'." );
            }
            context.Screen.DisplayHelp( _commands.GetForHelp( context.Screen.ScreenType, helpPath, null ),
                                        cmdLine,
                                        CKliRootEnv.GlobalOptions?.Invoke() ?? default,
                                        CKliRootEnv.GlobalFlags?.Invoke() ?? default );
            return ValueTask.FromResult( false );
        }
        try
        {
            // We are in a World.
            if( !world.Commands.TryFindForExecution( monitor, cmdLine, out var worldHelpPath )
                || cmdLine.FoundCommand == null
                || cmdLine.HasHelp )
            {
                if( cmdLine.FoundCommand == null && cmdLine.ExpectCommand )
                {
                    monitor.Error( $"Unknown command '{cmdLine.InitialAsStringArguments}'." );
                }
                // No luck or the --help is requested.
                // Displays the help in the context of the World. The World's commands
                // is extended with the CKli command: the help mixes the 2 kind of commands if
                // needed (based on the helpPath). If this second TryFindForExecution call
                // returned an help path, it should be the same or a better help path than
                // the one from only the CKli commands: use it.
                context.Screen.DisplayHelp( world.Commands.GetForHelp( context.Screen.ScreenType, worldHelpPath ?? helpPath, _commands ),
                                            cmdLine,
                                            CKliRootEnv.GlobalOptions?.Invoke() ?? default,
                                            CKliRootEnv.GlobalFlags?.Invoke() ?? default );
                return ValueTask.FromResult( cmdLine.HasHelp );
            }
            // We have a plugin command (and no --help).
            Throw.DebugAssert( "This cannot be a CKli command: we'd have located it initially.", cmdLine.FoundCommand.PluginTypeInfo != null );
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
        var result = await DoExecuteAsync( monitor, context, cmdLine );
        return result;

        static async Task<bool> DoExecuteAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
        {
            Throw.DebugAssert( cmdLine.FoundCommand != null );
            bool success;
            try
            {
                success = await cmdLine.FoundCommand.HandleCommandAsync( monitor, context, cmdLine );
            }
            catch( Exception ex )
            {
                using( monitor.OpenError( $"Unexpected error in command '{cmdLine.InitialAsStringArguments}'." ) )
                {
                    monitor.Error( ex );
                }
                return false;
            }

            if( success && !cmdLine.IsClosed )
            {
                // The command line has not been but the command handler returned true, it is buggy.
                // We return false (even if the handler claimed to be successful).
                monitor.Error( $"""
                The command '{cmdLine.FoundCommand.CommandPath}' implementation in '{cmdLine.FoundCommand.PluginTypeInfo?.TypeName ?? "CKli"}' is buggy.
                The command line MUST be closed before executing the command.
                """ );
                return false;
            }
            if( cmdLine.IsClosed && cmdLine.RemainingCount > 0 )
            {
                // The command line has been closed and there are remaining arguments.
                // If the command handler returned true, it is buggy.
                if( success )
                {
                    monitor.Error( $"""
                    The command '{cmdLine.FoundCommand.CommandPath}' implementation in '{cmdLine.FoundCommand.PluginTypeInfo?.TypeName ?? "CKli"}' is buggy.
                    Arguments remains in the command line but the command handler returned true.
                    """ );
                }
                // This displays the lovely header with remaining arguments.
                context.Screen.DisplayHelp( [new CommandHelp( context.Screen.ScreenType, cmdLine.FoundCommand )], cmdLine, default, default );
            }
            else if( !success && !cmdLine.IsClosed )
            {
                // The command failed and the command line has not been closed: this indicates a bad argument/option value
                // so we display the command help.
                // Before we must clear any remaining aruments otherwise we may display
                // a misleading remaining arguments message.
                cmdLine.CloseAndForgetRemaingArguments();
                context.Screen.DisplayHelp( [new CommandHelp( context.Screen.ScreenType, cmdLine.FoundCommand )], cmdLine, default, default );
            }
            // Not very elegant trick to cleanup 'ckli log' log files.
            if( success && cmdLine.FoundCommand is CKliLog )
            {
                CKliRootEnv.OnSuccessfulCKliLogCommand();
            }
            else
            {
                CKliRootEnv.OnAnyOtherCommandAndSuccess();
            }
            return success;
        }
    }
}
