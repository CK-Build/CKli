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
        monitor.Info( $"Executing '{cmdLine.InitialAsStringArguments}'." );

        // Honor the "ckli i" if specified.
        // The command will be handled below but with the InteractiveSreen (the command may use it)
        // and we will enter the interactive loop if this is the first command (History is empty and no
        // PreviousScreen exist) rather than returning from this method (FinalHandleInteractiveAsync does this).
        var interactiveScreen = context.Screen as InteractiveScreen;
        if( interactiveScreen == null && cmdLine.HasInteractiveArgument )
        {
            interactiveScreen = context.Screen.TryCreateInteractive( monitor, context );
            if( interactiveScreen != null )
            {
                context = interactiveScreen.Context;
            }
        }

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
                                        (interactiveScreen != null ? null : CKliRootEnv.GlobalOptions?.Invoke()) ?? default,
                                        (interactiveScreen != null ? null : CKliRootEnv.GlobalFlags?.Invoke()) ?? default );
            return FinalHandleInteractiveAsync( monitor, context, cmdLine, null, true );
        }
        // If it's a CKli command, we can now execute it.
        if( cmdLine.FoundCommand != null )
        {
            return ExecuteAsync( monitor, context, cmdLine, null );
        }
        // Not a CKli command. Opens the current World and tries to find a plugin command.
        var (stack, world) = StackRepository.TryOpenWorldFromPath( monitor, context, out bool error, skipPullStack: true );
        if( error )
        {
            // Don't enter interactive mode on error here.
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
                                        (interactiveScreen != null ? null : CKliRootEnv.GlobalOptions?.Invoke()) ?? default,
                                        (interactiveScreen != null ? null : CKliRootEnv.GlobalFlags?.Invoke()) ?? default );
            return FinalHandleInteractiveAsync( monitor, context, cmdLine, null, false );
        }

        // We are in a World, we have an opened Stack: handles World.Commands.
        return ExecuteWorldCommandAsync( monitor, context, cmdLine, helpPath, stack!, world );

        static async ValueTask<bool> ExecuteWorldCommandAsync( IActivityMonitor monitor,
                                                               CKliEnv context,
                                                               CommandLineArguments cmdLine,
                                                               string? helpPath,
                                                               StackRepository stack,
                                                               World world )
        {
            try
            {
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
                                                (context.Screen is InteractiveScreen ? null : CKliRootEnv.GlobalOptions?.Invoke()) ?? default,
                                                (context.Screen is InteractiveScreen ? null : CKliRootEnv.GlobalFlags?.Invoke()) ?? default );
                    return await FinalHandleInteractiveAsync( monitor, context, cmdLine, stack, cmdLine.HasHelp ).ConfigureAwait( false );
                }
                // We have a plugin command (and no --help).
                Throw.DebugAssert( "This cannot be a CKli command: we'd have located it initially.", cmdLine.FoundCommand.PluginTypeInfo != null );
                if( cmdLine.FoundCommand.IsDisabled )
                {
                    monitor.Error( $"Command '{cmdLine.FoundCommand.CommandPath}' exists but its type '{cmdLine.FoundCommand.PluginTypeInfo.TypeName}' is disabled in plugin '{cmdLine.FoundCommand.PluginTypeInfo.Plugin.FullPluginName}'." );
                    return await FinalHandleInteractiveAsync( monitor, context, cmdLine, stack, false ).ConfigureAwait( false );
                }
                return await ExecuteAsync( monitor, context, cmdLine, stack ).ConfigureAwait( false );
            }
            finally
            {
                stack?.Dispose();
            }
        }
    }

    static async ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine, StackRepository? initialStack )
    {
        Throw.DebugAssert( cmdLine.FoundCommand != null );
        var result = await DoExecuteAsync( monitor, context, cmdLine ).ConfigureAwait( false );
        return await FinalHandleInteractiveAsync( monitor, context, cmdLine, initialStack, result ).ConfigureAwait( false );

        static async Task<bool> DoExecuteAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
        {
            Throw.DebugAssert( cmdLine.FoundCommand != null );
            bool success;
            try
            {
                success = await cmdLine.FoundCommand.HandleCommandAsync( monitor, context, cmdLine ).ConfigureAwait( false );
                // The following result handling doesn't throw. We execute it here because on exception it is useless
                // to analyze the command line. 
                if( !cmdLine.IsClosed )
                {
                    if( success )
                    {
                        // The command line has not been closed but the command handler returned true, it is buggy.
                        // We return false (even if the handler claimed to be successful).
                        monitor.Error( $"""
                            The command '{cmdLine.FoundCommand.CommandPath}' implementation in '{cmdLine.FoundCommand.PluginTypeInfo?.TypeName ?? "CKli"}' is buggy.
                            The command line MUST be closed before executing the command.
                            """ );
                        success = false;
                    }
                    else
                    {
                        // The command failed and the command line has not been closed: this indicates a bad argument/option value
                        // so we display the command help.
                        // Before we must clear any remaining aruments otherwise we may display
                        // a misleading remaining arguments message.
                        cmdLine.CloseAndForgetRemaingArguments();
                        context.Screen.DisplayHelp( [new CommandHelp( context.Screen.ScreenType, cmdLine.FoundCommand )], cmdLine, default, default );
                    }
                }
                else if( cmdLine.RemainingCount > 0 )
                {
                    // The command line has been closed and there are remaining arguments.
                    // If the command handler returned true, it is buggy.
                    if( success )
                    {
                        monitor.Error( $"""
                            The command '{cmdLine.FoundCommand.CommandPath}' implementation in '{cmdLine.FoundCommand.PluginTypeInfo?.TypeName ?? "CKli"}' is buggy.
                            Arguments remains in the command line but the command handler returned true.
                            """ );
                        // We consider that this is an error.
                        success = false;
                    }
                    // This displays the lovely header with remaining arguments.
                    context.Screen.DisplayHelp( [new CommandHelp( context.Screen.ScreenType, cmdLine.FoundCommand )], cmdLine, default, default );
                }
            }
            catch( Exception ex )
            {
                using( monitor.OpenError( $"Unexpected error in command '{cmdLine.InitialAsStringArguments}'." ) )
                {
                    monitor.Error( ex );
                }
                success = false;
            }
            if( success )
            {
                context.Screen.Display( t => t.Text( "❰✓❱", ConsoleColor.Black, ConsoleColor.DarkGreen ) );
            }
            else
            {
                context.Screen.Display( t => t.Text( "❌ Failed", ConsoleColor.Black, ConsoleColor.Red ) );
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

    static ValueTask<bool> FinalHandleInteractiveAsync( IActivityMonitor monitor,
                                                        CKliEnv context,
                                                        CommandLineArguments cmdLine,
                                                        StackRepository? initialStack,
                                                        bool initialResult )
    {
        if( initialStack != null )
        {
            if( initialResult )
            {
                initialStack.Close( monitor );
            }
            else
            {
                initialStack.Dispose();
            }
        }
        if( context.Screen is InteractiveScreen interactive )
        {
            // Always handle the current log file: this avoids saving the
            // log file for "ckli log" or help display.
            CKliRootEnv.OnInteractiveCommandExecuted( monitor, cmdLine );
            // If we are initiating the interactive mode, enter its loop:
            // this will return with the "exit" command.
            if( interactive.PreviousScreen == null )
            {
                return new ValueTask<bool>( interactive.RunInteractiveAsync( monitor, cmdLine ) );
            }
        }
        return ValueTask.FromResult( initialResult );
    }
}
