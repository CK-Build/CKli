using CK.Core;
using CKli.Core;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Opens a Conformant SVersion branch.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branchName">The branch name to open.</param>
    /// <param name="parent">Parent branch to consider instead of the currently checked out branch (applies only to 'explo/' branch).</param>
    /// <returns>True on success, false on error.</returns>
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
                            string? parent = null )
    {
        var repos = World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( repos == null ) return false;

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
                parent = repos[0].GitRepository.CurrentBranchName;
                for( int i = 1; i < repos.Count; i++ )
                {
                    Repo? repo = repos[i];
                    if( parent != repo.GitRepository.CurrentBranchName )
                    {
                        monitor.Error( $"""
                            Currently checked out branch is not the same across the repositories. The option --parent must be specified with the branch name.
                            At least, '{repos[0].GitRepository.DisplayPath}' is on '{parent}' and '{repo.DisplayPath}' is on '{repo.GitRepository.CurrentBranchName}'.
                            """ );
                        return false;
                    }
                }
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
        foreach( var repo in repos )
        {
            var info = Create( monitor, repo, ns, _autoFixUselessBranch );
            if( info == null ) return false;
            if( info.HasIssue )
            {
                monitor.Error( $"Please fix any issue in '{repo.DisplayPath}' before opening a new branch." );
                return false;
            }
            var b = info.Branches[newBranch.Index];
            // Since we explicitly open the branch here, we want the "dev/" to exist (and be checked out).
            if( !b.EnsureExists( monitor ) ) return false;
            b.EnsureDevBranch();
            if( !b.Synchronize( monitor, _commitProvider ) ) return false;
            if( !repo.GitRepository.Checkout(monitor,b.GitDevBranch) ) return false;
        }
        // Everything went fine: save the updated namespace if needed.
        return !namespaceChanged || SaveBranchNamespace( monitor, ns );
    }

}

