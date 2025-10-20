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
                "Lists the World's Repos folder and remote origin.",
                [], [], [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && DisplayRepos( monitor, context ) );
    }

    static bool DisplayRepos( IActivityMonitor monitor, CKliEnv context )
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
        return screenType.Text( repo.DisplayPath ).HyperLink( new System.Uri( $"file:///{repo.WorkingFolder}" ) ).Box( marginRight: 1 )
                        .AddRight( screenType.Text( repo.OriginUrl.ToString() ) ).HyperLink( repo.OriginUrl );
    }
}
