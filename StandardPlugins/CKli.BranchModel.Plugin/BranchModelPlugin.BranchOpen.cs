using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Implements "ckli branch open" (the command is handled by the HotZone plugin, that provides the
    /// <paramref name="versionResolver"/>): opens a Conformant SVersion branch, or updates the link type of an opened
    /// one, in the repositories of the current directory (see <see cref="HotBranch.OpenOrPredict"/>).
    /// <para>
    /// Only the opened branch and its closest existing parent matter: a repository where one of them has an issue is
    /// skipped and fails the command, the other ones are still opened. A failed command doesn't save the branch model:
    /// running it again once the failures are fixed completes the open (the branches already created are kept).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branchName">The branch name to open.</param>
    /// <param name="link">
    /// Optional link type (Manual, Regular, CI or Full) to the parent branch. Defaults to Full for a new branch: an already
    /// opened branch keeps its current link type.
    /// </param>
    /// <param name="parent">Parent branch to consider instead of the currently checked out branch (applies only to 'explo/' branch).</param>
    /// <param name="versionResolver">
    /// Optional provider of the resolver of the package versions that conflict (see <see cref="HotBranch.Synchronize"/>).
    /// It receives the opened branch.
    /// </param>
    /// <param name="dryRun">
    /// True to only display what the open would do: nothing is created nor merged and the branch model is not changed.
    /// This returns what the open would return: false when a merge would be left in progress or when it would fail.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public bool OpenBranch( IActivityMonitor monitor,
                            CKliEnv context,
                            string branchName,
                            string? link,
                            string? parent,
                            Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? versionResolver,
                            bool dryRun = false )
    {
        var repos = World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( repos == null ) return false;

        if( !ParseLink( monitor, link, allowManual: true, out var linkType ) )
        {
            return false;
        }

        // The system state is... what it is.
        // We can have a git branch and/or a BranchName: we must not rely here on any kind of synchronization
        // between these 2 aspects.

        // First, handle the branch namespace because it is a immutable model. The namespace is saved only if
        // the git branches have been handled.
        if( !BranchName.TryParseBranchName( monitor, branchName, out CSVersionKind csPrerelease ) )
        {
            return false;
        }

        BranchNamespace ns;
        BranchName newBranch;

        if( csPrerelease != CSVersionKind.None )
        {
            (ns, newBranch) = _namespace.AddOrUpdate( linkType, csPrerelease );
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
            (ns, newBranch) = _namespace.AddOrUpdateExplo( branchName, linkType, parentBranch );
        }
        bool namespaceChanged = !ns.Equals( _namespace );
        if( namespaceChanged )
        {
            var added = ns.Branches.Length > _namespace.Branches.Length;
            var change = dryRun
                            ? (added ? "Would add new" : "Would update")
                            : (added ? "Added new" : "Updated");
            monitor.Info( ScreenType.ScreenTag, $"""
                {change} branch model:
                {newBranch.ToParentedString()}
                """ );
        }
        Func<IActivityMonitor, IPackageVersionResolver?>? resolver = versionResolver != null
                                                                        ? m => versionResolver( m, newBranch )
                                                                        : null;
        // To handle git branches, we create brand new BranchModelInfo (with their HotBranches) that
        // are driven by the new namespace.
        var infos = GetInfos( monitor, repos, ns );
        if( infos == null ) return false;
        var report = new BranchMergeReport( dryRun, prepareMerges: resolver != null );
        bool success = OpenOrPredict( monitor, infos, newBranch, resolver, report, $"opening '{newBranch.Name}'" );
        report.Display( context.Screen );
        return success && (!namespaceChanged || dryRun || SaveBranchNamespace( monitor, ns ));
    }

    // Opens the branch in each repository ("ckli branch open" and "ckli branch switch --create"): a repository where the
    // branch or its closest existing parent has an issue is skipped and fails, the other ones are still opened.
    static bool OpenOrPredict( IActivityMonitor monitor,
                               BranchModelInfo[] infos,
                               BranchName branch,
                               Func<IActivityMonitor, IPackageVersionResolver?>? resolver,
                               BranchMergeReport report,
                               string before )
    {
        bool success = true;
        foreach( var info in infos )
        {
            var b = info.Branches[branch.Index];
            success &= b.CheckNoIssue( monitor, before ) && b.OpenOrPredict( monitor, resolver, report );
        }
        return success;
    }

    static bool ParseLink( IActivityMonitor monitor, string? link, bool allowManual, out BranchLinkType linkType )
    {
        linkType = BranchLinkType.None;
        var sMode = link.AsSpan();
        if( sMode.Length > 0 )
        {
            if( !BranchLinkTypeExtensions.TryMatchLinkType( ref sMode, out linkType )
                || sMode.Length > 0
                || (!allowManual && linkType is BranchLinkType.Manual) )
            {
                monitor.Error( $"Invalid link type '{link}'. Must be {(allowManual ? "Manual, Regular, CI or Full." : "Regular, CI or Full.")}" );
                return false;
            }
        }
        return true;
    }

}

