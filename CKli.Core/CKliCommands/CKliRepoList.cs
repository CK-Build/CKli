using CK.Core;
using CKli.Core;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliRepoList : Command
{
    public CKliRepoList()
        : base( null,
                "repo list",
                "Lists the World's Repos folder, current branch name, remote commit diffs and remote origin url.",
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
            world.SetExecutingCommand( command );
            var repos = all
                    ? world.GetAllDefinedRepo( monitor )
                    : world.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
            if( repos == null ) return false;

            IRenderable display = context.RenderableUnit;
            var screenType = display.ScreenType;

            //IRenderable display = new Collapsable( screenType.Text( $"Stack:" )
            //                                                 .AddRight( screenType.Text( stack.StackName ).Box( marginLeft:1, foreColor:ConsoleColor.White )
            //                                                 .AddRight( screenType.Text( stack.StackWorkingFolder.LastPart ).HyperLink( new Uri( stack.StackWorkingFolder ) ) ) )
            //                                                 .AddBelow( screenType.Text( stack.OriginUrl.ToString() ).HyperLink( stack.OriginUrl ) ) );

            if( byBranch )
            {
                display = display.AddBelow(
                    repos.GroupBy( r => r.GitStatus.CurrentBranchName )
                         .OrderBy( g => g.Key )
                         .Select( g => new Collapsable(
                             screenType.Text( g.Key )
                             .AddBelow( screenType.Unit.AddBelow( g.OrderBy( r => r.Index )
                                                                   .Select( r => r.ToRenderable( screenType, false, true, true ) ) )
                                                       .TableLayout() ) )
                         ) );
            }
            else
            {
                display = display.AddBelow( repos.Select( r => r.ToRenderable( screenType, true, true, true ) ) )
                                 .TableLayout();
            }
            context.Screen.Display( display );
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
