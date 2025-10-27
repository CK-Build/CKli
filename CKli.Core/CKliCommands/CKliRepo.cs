using CK.Core;
using CKli.Core;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliRepo : Command
{
    public CKliRepo()
        : base( null,
                "repo",
                "Lists the World's Repos folder, current branch name, remote commit diffs and remote origin url.",
                [], [],
                [
                    (["--by-branch,-b"], "Group by current branch names.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool byBranch = cmdLine.EatFlag( "--by-branch", "-b" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && DisplayRepos( monitor, context, byBranch ) );
    }

    static bool DisplayRepos( IActivityMonitor monitor, CKliEnv context, bool byBranch )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                context,
                                                out var stack,
                                                out var world,
                                                skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            var repos = world.GetAllDefinedRepo( monitor );
            if( repos == null ) return false;

            var screenType = context.Screen.ScreenType;
            IRenderable display;
            if( byBranch )
            {
                display = screenType.Unit.AddBelow(
                    repos.GroupBy( r => r.GitStatus.CurrentBranchName )
                         .OrderBy( g => g.Key )
                         .Select( g => new Collapsable(
                             screenType.Text( g.Key )
                             .AddBelow( screenType.Unit.AddBelow( g.Select( r => r.ToRenderable( screenType, true, true, true ) ) )
                                        .TableLayout() ) )
                         ) );
            }
            else
            {
                display = context.RenderableUnit
                                 .AddBelow( repos.Select( r => r.ToRenderable( screenType, true, true, true ) ) )
                                 .TableLayout();
            }
            context.Screen.Display( display );
            return true;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
