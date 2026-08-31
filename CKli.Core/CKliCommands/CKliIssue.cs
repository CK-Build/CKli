using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Raises the <see cref="WorldEvents.Issue"/> event.
/// </summary>
public sealed class CKliIssue : Command
{
    internal CKliIssue()
        : base( null,
                "issue",
                "Asks plugins to detect any possible issues.",
                arguments: [],
                options: [],
                [
                    (["--all"], "Consider all the Repos of the current World (even if current path is in a Repo)."),
                    (["--fix"], "Fix all the issues found.")
                ] )
    {
    }

    /// <summary>
    /// Executes the "ckli issue" command: raises the <see cref="WorldEvents.Issue"/> event and displays the issues found.
    /// If the "--fix" option is specified, it tries to fix all the issues that can be fixed automatically.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="cmdLine">The command line.</param>
    /// <param name="scopeAlive">Cancellation token from the current <see cref="InterruptibleScope"/>.</param>
    /// <returns>True on success, false on error.</returns>
    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool fix = cmdLine.EatFlag( "--fix" );
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        return IssueAsync( monitor, this, context, all, fix, scopeAlive );
    }

    static async ValueTask<bool> IssueAsync( IActivityMonitor monitor, Command command, CKliEnv context, bool all, bool fix, CancellationToken scopeAlive )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command, scopeAlive );
            var issues = new List<World.Issue>();
            if( all )
            {
                var layout = world.CreateLayoutIssue( monitor, context.Screen );
                if( layout != null ) issues.Add( layout );
            }
            var disabledPlugin = world.GetDisabledPluginsHeader();
            if( disabledPlugin != null )
            {
                monitor.Warn( disabledPlugin );
                return true;
            }
            IReadOnlyList<Repo>? repos = all
                                          ? world.GetAllDefinedRepo( monitor )
                                          : world.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
            if( repos != null 
                && world.Events.SafeRaiseEvent( monitor, new IssueEventArgs( monitor, context, world, repos, issues ) )
                && await world.HandleIssuesAsync( monitor, context, issues, displayIssues: !fix, fix, scopeAlive ).ConfigureAwait( false ) )
            {
                // On success, saves the World's DefinitionFile if it's dirty.
                return stack.Close( monitor );
            }
            return false;
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }
}
