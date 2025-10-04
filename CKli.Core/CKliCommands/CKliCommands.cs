using CK.Core;
using CKli.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
