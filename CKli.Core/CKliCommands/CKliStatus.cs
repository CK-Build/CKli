using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliStatus : Command
{
    public CKliStatus()
        : base( null,
                "status",
                """
                In a stack lists the World's Repos folder, current branch name, remote commit diffs and remote origin url.
                Outside a stack, lists all the stacks that exist locally.
                """,
                [], [],
                [
                    (["--by-branch,-b"], "Group by current branch names."),
                    (["--all"], "Lists all the Repos of the current World (even if current path is in a Repo).")
               ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool byBranch = cmdLine.EatFlag( "--by-branch", "-b" );
        bool all = cmdLine.EatFlag( "--all" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && DisplayRepos( monitor, this, context, byBranch, all ) );
    }

    static bool DisplayRepos( IActivityMonitor monitor, Command command, CKliEnv context, bool byBranch, bool all )
    {
        var (stack, world) = StackRepository.TryOpenWorldFromPath( monitor, context, out var error, skipPullStack: true );
        if( error )
        {
            return false;
        }
        try
        {
            var screen = context.Screen.ScreenType;
            IRenderable display;
            if( stack == null )
            {
                display = RenderStackRegistry( monitor, screen );
            }
            else
            {
                Throw.DebugAssert( world != null );
                world.SetExecutingCommand( command );
                var repos = all
                        ? world.GetAllDefinedRepo( monitor )
                        : world.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
                if( repos == null ) return false;

                all = repos.Count == world.Layout.Count;
                var repoCount = all
                                ? $"({world.Layout.Count} repositories)"
                                : $"({repos.Count} out of {world.Layout.Count} repositories)";

                display = new Collapsable( screen.Text( $"{(stack.IsPublic ? "Public" : "Private")} stack", foreColor: ConsoleColor.Gray )
                                                     .AddRight( screen.Text( stack.StackName )
                                                                      .Box( marginLeft: 1, foreColor: ConsoleColor.White, marginRight: 1 )
                                                                      .AddRight( screen.Text( repoCount ) ) )
                                            .AddBelow( screen.Text( stack.StackWorkingFolder )
                                                             .HyperLink( new Uri( stack.StackWorkingFolder ) )
                                                              .Box( marginLeft: 1, foreColor: ConsoleColor.DarkGreen ),
                                                       screen.Text( stack.OriginUrl.ToString() )
                                                             .HyperLink( stack.OriginUrl )
                                                             .Box( marginLeft: 1, foreColor: ConsoleColor.DarkBlue ) ) );

                if( byBranch )
                {
                    display = display.AddBelow(
                        repos.GroupBy( r => r.GitStatus.CurrentBranchName )
                             .OrderBy( g => g.Key )
                             .Select( g => new Collapsable(
                                 screen.Text( g.Key )
                                 .AddBelow( screen.Unit.AddBelow( g.OrderBy( r => r.Index )
                                                                       .Select( r => r.ToRenderable( screen, false, true, true ) ) )
                                                           .TableLayout() ) )
                             ) );
                }
                else
                {
                    var list = screen.Unit.AddBelow( repos.Select( r => r.ToRenderable( screen, true, true, true ) ) )
                                          .TableLayout();
                    display = display.AddBelow( list );
                }
            }
            context.Screen.Display( display );
            return true;
        }
        finally
        {
            stack?.Dispose();
        }
    }

    static IRenderable RenderStackRegistry( IActivityMonitor monitor, ScreenType screenType )
    {
        IRenderable display;
        var stacks = StackRepository.ReadRegistry( monitor )
                                                    .Select( Expand )
                                                    .GroupBy( s => s.Url )
                                                    .Select( g => (Url: g.Key, IsPrivate: UnifyBool( g.Select( p => p.IsPrivate ) ), Paths: g.OrderBy( s => s.IsDuplicate ).ThenBy( s => s.Path )) );
        display = screenType.Unit;
        foreach( var g in stacks )
        {
            display = display.AddBelow( new Collapsable( screenType.Text( g.Url.ToString(), foreColor: ConsoleColor.DarkBlue ).HyperLink( g.Url )
                                                                   .AddRight( screenType.Text( g.IsPrivate switch
                                                                                                {
                                                                                                    true => "[Private]",
                                                                                                    false => "[Public]",
                                                                                                    _ => "[⚠Public/Private]"
                                                                                                },
                                                                                                foreColor: g.IsPrivate is null
                                                                                                            ? ConsoleColor.Red
                                                                                                            : ConsoleColor.DarkGray )
                                                                                        .Box( marginLeft: 1 ) )
                                                         .AddBelow( g.Paths.Select( p => OnePath( screenType, p.Path, p.IsDuplicate ) ) ) ) );
        }

        static (Uri Url, NormalizedPath Path, bool IsDuplicate, bool IsPrivate) Expand( KeyValuePair<NormalizedPath, Uri> kv )
        {
            var isPrivate = kv.Key.LastPart.Equals( StackRepository.PrivateStackName, StringComparison.OrdinalIgnoreCase );
            var path = kv.Key.RemoveLastPart();
            var name = path.LastPart;
            bool isDuplicate = false;
            if( name.StartsWith( StackRepository.DuplicatePrefix, StringComparison.OrdinalIgnoreCase ) )
            {
                name = name.Substring( StackRepository.DuplicatePrefix.Length );
                isDuplicate = true;
            }
            return (kv.Value, path, isDuplicate, isPrivate);
        }

        static IRenderable OnePath( ScreenType screen, NormalizedPath path, bool isDuplicate )
        {
            var p = screen.Text( path.RemoveLastPart() + '/', foreColor: ConsoleColor.DarkGray )
                             .AddRight( screen.Text( path.LastPart, foreColor: isDuplicate ? ConsoleColor.Gray : ConsoleColor.White ) );

            return p.HyperLink( new Uri( path ) );
        }

        static bool? UnifyBool( IEnumerable<bool> enumerable )
        {
            bool allTrue = enumerable.All( Util.FuncIdentity );
            bool allFalse = enumerable.All( x => !x );
            return allTrue
                    ? true
                    : allFalse
                        ? false
                        : null;
        }

        return display;
    }
}
