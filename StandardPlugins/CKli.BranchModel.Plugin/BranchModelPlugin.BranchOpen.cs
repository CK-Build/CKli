using CK.Core;
using CKli.Core;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    [Description( """
        Opens a Conformant SVersion branch if it doesn't already exist.
        - For prerelease branches ('alpha', 'bravo', 'charlie', ...'zulu'), the parent branch is based on the lexicographic order.
        - For exploratory branches ('explo/name'), the parent is the currently checked out branch (unless --parent option specifies it). 
        """ )]
    [CommandPath( "branch open" )]
    public bool BranchOpen( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Branch name to open." )]
                            string branchName,
                            [Description( "Parent branch to consider instead of the currently checked out branch (applies only to 'explo/' branch)." )]
                            string? parent )
    {
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null ) return false;

        // The system state is... what it is.
        // We can have a git branch and/or a BranchName: we must not rely here on any kind of synchronization
        // between these 2 aspects.

        // First, handle the branch namespace because it is a immutable model. The namespace will be updated
        // only if git branch manipulations/synchronizations below work.
        if( !BranchName.TryParseBranchName( monitor, branchName, out var csPrerelease ) )
        {
            return false;
        }

        BranchNamespace ns;
        BranchName newBranch;

        if( csPrerelease != CSVersionKind.None )
        {
            (ns, newBranch) = _namespace.AddOrUpdate( BranchLinkType.CI, csPrerelease );
        }
        else
        {
            var what = "";
            if( string.IsNullOrWhiteSpace( parent ) )
            {
                parent = repo.GitRepository.CurrentBranchName;
                what = "the currently checked out branch ";
            }
            var parentBranch = _namespace.Find( parent );
            if( parentBranch == null )
            {
                monitor.Error( $"""
                    Unable to consider {what}'{parent}' to be the parent of the new '{branchName}' branch.
                    It must be an opened branch: opened branches are '{_namespace.Branches.Select( b => b.Name ).Concatenate( "', '" )}'.
                    """ );
                return false;
            }
            (ns, newBranch) = _namespace.AddOrUpdateExplo( branchName, BranchLinkType.CI, parentBranch );
        }
        bool namespaceChanged = !ns.Equals( _namespace );
        if( namespaceChanged )
        {
            var added = ns.Branches.Length > _namespace.Branches.Length;
            monitor.Info( ScreenType.CKliScreenTag, $"""
                {(added ? "Added new" : "Updated")} branch model:
                {newBranch.ToParentedString()}
                """ );
        }
        // To handle git branches, we create a BranchModelInfo (with the HotBranch) that is driven by the new namespace.
        var info = Create( monitor, repo, ns, _autoFixUselessBranch );
        if( info == null ) return false;
        if( info.HasIssue )
        {
            monitor.Error( "Please fix any issue before opening a new branch." );
            return false;
        }
        var b = info.Branches[newBranch.Index];
        if( !b.EnsureExists( monitor )
            || !b.Synchronize( monitor, _commitProvider ) )
        {
            return false;
        }
        // Everything went fine: save the updated namespace if needed.
        return !namespaceChanged || SaveBranchNamespace( monitor, ns );
    }

}

