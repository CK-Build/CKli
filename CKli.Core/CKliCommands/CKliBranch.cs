using CK.Core;
using CKli.Core;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliBranch : Command
{
    public CKliBranch()
        : base( null,
                "branch",
                "Lists the World's Repos current branch status.",
                [],
                [],
                [
                    (["--group,-g"], "Group by branch names.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool group = cmdLine.EatFlag( "--group", "-g" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && DisplayBranches( monitor, context, group ) );
    }

    static bool DisplayBranches( IActivityMonitor monitor, CKliEnv context, bool group )
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
            var display = context.RenderableUnit.AddBelow( repos.Select( r => ToRenderable( context.Screen.ScreenType, r ) ) );
            context.Screen.Display( display );
            return true;
        }
        finally
        {
            stack.Dispose();
        }
    }

    static IRenderable ToRenderable( ScreenType screenType, Repo repo )
    {
        IRenderable r = screenType.Text( repo.DisplayPath ).HyperLink( new System.Uri( $"file:///{repo.WorkingFolder}" ) );
        var status = repo.GitStatus;
        if( status.IsDirty ) r = r.Box( marginRight: 1 ).AddLeft( screenType.Text( "⋆" ) );
        else r = r.Box( marginLeft: 1, marginRight: 1 );
        r = r.AddRight( screenType.Text( status.CurrentBranchName ) ).Box( marginRight: 1, style: new TextStyle( TextEffect.Bold ) );
        if( status.IsTracked )
        {
            r = r.AddRight( CommitDiff( screenType, '↑', status.CommitAhead.Value ) );
            r = r.AddRight( CommitDiff( screenType, '↓', status.CommitBehind.Value ) );
        }
        else
        {
            r = r.AddRight( screenType.Text( "<local>" ) );
        }
        return r;

        static IRenderable CommitDiff( ScreenType screenType, char aheadOrBehind, int count )
        {
            return screenType.Text( $"{aheadOrBehind}{count}",
                                    count != 0
                                        ? new TextStyle( new Color( System.ConsoleColor.Red, System.ConsoleColor.Black ), TextEffect.Bold )
                                        : TextStyle.None );
        }
    }
}
