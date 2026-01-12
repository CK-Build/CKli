using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;
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

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool fix = cmdLine.EatFlag( "--fix" );
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        return IssueAsync( monitor, this, context, all, fix );
    }

    static async ValueTask<bool> IssueAsync( IActivityMonitor monitor, Command command, CKliEnv context, bool all, bool fix )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command );
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
                                          : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
            if( repos == null ) return false;

            if( repos.Count > 0 && !world.Events.SafeRaiseEvent( monitor, new IssueEvent( monitor, world, repos, issues ) ) )
            {
                return false;
            }
            if( issues.Count == 0 )
            {
                monitor.Info( ScreenType.CKliScreenTag, "No issues found." );
            }
            else
            {
                int autoFixCount = issues.Count( i => !i.ManualFix );
                int manualFixCount = issues.Count - autoFixCount;
                if( manualFixCount > 0 )
                {
                    monitor.Warn( $"Found {issues.Count} issues that require a manual fix." );
                }
                if( fix )
                {
                    // Applies always the same ordering.
                    var groupedIssues = issues.GroupBy( i => i.Repo ).OrderBy( g => g.Key?.Index ?? -1 );
                    if( autoFixCount > 0 )
                    {
                        using( monitor.OpenInfo( manualFixCount > 0
                                                    ? $"Trying to fix {autoFixCount} issues ({manualFixCount} issues must be fixed manually)."
                                                    : $"Trying to fix {autoFixCount} issues." ) )
                        {
                            foreach( var g in groupedIssues.Where( g => g.Any() ) )
                            {
                                using( monitor.OpenInfo( $"Handling issues for '{g.Key?.DisplayPath ?? world.Name.FullName}'." ) )
                                {
                                    foreach( var i in g )
                                    {
                                        try
                                        {
                                            if( !i.ManualFix && !await i.ExecuteAsync( monitor, context, world ).ConfigureAwait( false ) )
                                            {
                                                return false;
                                            }
                                        }
                                        catch( Exception ex )
                                        {
                                            monitor.Error( ex );
                                            return false;
                                        }
                                    }
                                }
                            }
                        }
                        // Consider that the final result requires no error when saving a dirty World's DefinitionFile.
                        return stack.Close( monitor );
                    }
                }
                else
                {
                    foreach( var g in issues.GroupBy( i => i.Repo ).OrderBy( g => g.Key?.Index ?? -1 ) )
                    {
                        var link = g.Key == null
                                    ? context.Screen.ScreenType.Text( world.Name.FullName )
                                        .HyperLink( new Uri( world.Name.WorldRoot ) )
                                    : context.Screen.ScreenType.Text( g.Key.DisplayPath )
                                        .HyperLink( new Uri( g.Key.WorkingFolder ) );
                        var header = link.Box( marginRight: 1 ).AddRight( context.Screen.ScreenType.Text( $"({g.Count()})", TextEffect.Italic ) );
                        var repo = header.AddBelow( g.Select( i => i.ToRenderable( context.Screen.ScreenType ) ) );
                        context.Screen.Display( new Collapsable( repo ) );
                    }
                }
            }
            // Consider that the final result requires no error when saving a dirty World's DefinitionFile.
            return stack.Close( monitor );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }

}
