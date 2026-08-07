using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

sealed class MissingRootBranchIssue : World.Issue
{
    readonly HotBranch _root;
    readonly Branch _prevRoot;

    MissingRootBranchIssue( string title, IRenderable body, HotBranch root, Branch mainOrMaster )
        : base( title, body, root.Repo )
    {
        _root = root;
        _prevRoot = mainOrMaster;
    }

    public static World.Issue Create( IActivityMonitor monitor,
                                      HotBranch root,
                                      Branch? prevRoot,
                                      ScreenType screenType )
    {
        var title = $"Missing root branch '{root.BranchName.Name}'.";
        if( prevRoot == null )
        {
            return CreateManual( title, screenType.Text( $"""
                    No 'master' nor 'main' branch found.
                    The '{root.BranchName.Name}' should be created manually.
                    """ ), root.Repo );
        }
        return new MissingRootBranchIssue( title,
                                           screenType.Text( $"Can be fixed by creating it from '{prevRoot.FriendlyName}'." ),
                                           root,
                                           prevRoot );
    }

    protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world, CancellationToken cancellation )
    {
        Throw.DebugAssert( Repo != null );
        BranchLink.CreateAheadBranch( Repo.GitRepository, _prevRoot.Tip, _root.BranchName.Name, withEmptyInitializationCommit: true );
        return ValueTask.FromResult( true );
    }
}
