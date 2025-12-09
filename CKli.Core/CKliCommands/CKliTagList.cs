using CK.Core;
using CKli.Core;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Core.ActivityMonitor;

namespace CKli;

sealed class CKliTagList : Command
{
    public CKliTagList()
        : base( null,
                "tag list",
                """
                Lists local tags and/or remote tags from the current Repo or all the Repos.
                """,
                [],
                [],
                [
                    (["--local"], "Local tags only."),
                    (["--remote"], "Remote tags only."),
                    (["--all"], "Lists for all the Repos of the current World (even if current path is in a Repo).")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool remote = cmdLine.EatFlag( "--remote" );
        bool local = cmdLine.EatFlag( "--local" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && ListTags( monitor, context, all, local, remote ) );
    }

    static bool ListTags( IActivityMonitor monitor, CKliEnv context, bool all, bool local, bool remote )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                context,
                                                out var stack,
                                                out var world,
                                                skipPullStack: true ) )
        {
            return false;
        }
        var s = context.Screen.ScreenType;
        try
        {
            var repos = all
                        ? world.GetAllDefinedRepo( monitor )
                        : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
            if( repos == null ) return false;
            foreach( var repo in repos )
            {
                if( local )
                {
                    if( !repo.GitRepository.GetLocalTags( monitor, out var tags ) )
                    {
                        return false;
                    }
                    var link = s.Text( repo.DisplayPath )
                                    .HyperLink( new Uri( $"file://{repo.WorkingFolder}" ) );
                    var header = link.Box( marginRight: 1 ).AddRight( s.Text( $"{tags.Tags.Length} local tags.", effect: TextEffect.Italic| TextEffect.Bold ) );
                    var lines = s.Unit.AddBelow( tags.GroupedTags.Select( t => t.ToRenderable( s ) ) );
                    context.Screen.Display( new Collapsable( header.AddBelow( lines.TableLayout() ) ) );
                }
                if( remote )
                {
                    if( !repo.GitRepository.GetRemoteTags( monitor, out var tags ) )
                    {
                        return false;
                    }
                    var link = s.Text( repo.OriginUrl.ToString() )
                                    .HyperLink( repo.OriginUrl );
                    var header = link.Box( marginRight: 1 ).AddRight( s.Text( $"{tags.Tags.Length} remote tags.", effect: TextEffect.Italic| TextEffect.Bold ) );
                    var lines = s.Unit.AddBelow( tags.GroupedTags.Select( t => t.ToRenderable( s ) ) );
                    context.Screen.Display( new Collapsable( header.AddBelow( lines.TableLayout() ) ) );
                }
                if( !local && !remote )
                {
                    if( !repo.GitRepository.GetLocalTags( monitor, out var localTags ) )
                    {
                        return false;
                    }
                    if( !repo.GitRepository.GetRemoteTags( monitor, out var remoteTags ) )
                    {
                        return false;
                    }
                    var diff = new GitTagInfo.Diff( localTags, remoteTags );

                    var link = s.Text( repo.DisplayPath )
                                    .HyperLink( new Uri( $"file://{repo.WorkingFolder}" ) );
                    var header = link.Box( marginRight: 1 ).AddRight( s.Text( $"{localTags.Tags.Length} local tags / {remoteTags.Tags.Length} remote tags.", effect: TextEffect.Italic | TextEffect.Bold ) );
                    var lines = s.Unit.AddBelow( diff.Entries.Select( t => t.ToRenderable( s ) ) );
                    context.Screen.Display( new Collapsable( header.AddBelow( lines.TableLayout() ) ) );

                }
            }
            return true;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
