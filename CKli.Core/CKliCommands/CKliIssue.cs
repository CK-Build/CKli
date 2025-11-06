using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliIssue : Command
{
    public CKliIssue()
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
        return IssueAsync( monitor, context, all, fix );
    }

    static ValueTask<bool> IssueAsync( IActivityMonitor monitor, CKliEnv context, bool all, bool fix )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return ValueTask.FromResult( false );
        }
        try
        {
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
                return ValueTask.FromResult( true );
            }
            IReadOnlyList<Repo>? repos = all
                                          ? world.GetAllDefinedRepo( monitor )
                                          : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
            if( repos == null ) return ValueTask.FromResult( false );

            if( repos.Count > 0 && !world.Events.SafeRaiseEvent( monitor, new IssueEvent( monitor, world, repos, issues ) ) )
            {
                return ValueTask.FromResult( false );
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
                    if( autoFixCount > 0 )
                    {
                        using( monitor.OpenInfo( manualFixCount > 0
                                                    ? $"Trying to fix {autoFixCount} issues ({manualFixCount} issues must be fixed manually)."
                                                    : $"Trying to fix {autoFixCount} issues." ) )
                        {
                            return new ValueTask<bool>( DoFixAsync( monitor, context, world, issues ) );
                        }
                    }
                }
                else
                {
                    foreach( var g in issues.GroupBy( i => i.Repo ) )
                    {
                        var link = g.Key == null
                                    ? context.Screen.ScreenType.Text( world.Name.FullName )
                                        .HyperLink( new Uri( $"file://{world.Name.WorldRoot}" ) )
                                    : context.Screen.ScreenType.Text( g.Key.DisplayPath )
                                        .HyperLink( new Uri( $"file://{g.Key.WorkingFolder}" ) );
                        var header = link.Box( marginRight: 1 ).AddRight( context.Screen.ScreenType.Text( $"({g.Count()})", new TextStyle( TextEffect.Italic ) ) );
                        var repo = header.AddBelow( g.Select( i => i.ToRenderable( context.Screen.ScreenType ) ) );
                        context.Screen.Display( new Collapsable( repo ) );
                    }
                }
            }
            return ValueTask.FromResult( true );
        }
        finally
        {
            stack.Dispose();
        }
    }

    static async Task<bool> DoFixAsync( IActivityMonitor monitor, CKliEnv context, World world, List<World.Issue> issues )
    {
        foreach( var i in issues )
        {
            try
            {
                if( !i.ManualFix && !await i.ExecuteAsync( monitor, context, world ) )
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
        return true;
    }
}
