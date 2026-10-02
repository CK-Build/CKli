using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Closes a Conformant SVersion branch in the repositories of the current directory: it is integrated in its
    /// closest existing parent. The upstreams whose versions of the branch are consumed by a closed repository are
    /// also closed. The branch leaves the branch model when no repository of the World has it anymore.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branchName">The branch name to close.</param>
    /// <param name="discard">True to keep the branch where it is and not integrate it in its closest parent.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Closes a Conformant SVersion branch in the current repositories by integrating it in its closest parent.
        The upstreams whose versions of this branch are consumed are also closed. The branch is removed from the
        branch model when no repository of the World has it anymore.
        """ )]
    [CommandPath( "branch close" )]
    public bool BranchClose( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description( "Branch name to close." )]
                             string branchName,
                             [Description( "Only remove the branch from the branch model (must be run at the root of the World): the Git branches are left as-is." )]
                             bool discard )
    {
        var b = _namespace.FindRequired( monitor, branchName );
        if( b == null )
        {
            return false;
        }
        if( b.IsRoot )
        {
            monitor.Error( "The root branch cannot be closed." );
            return false;
        }
        if( discard )
        {
            // The branch model is World global and nothing is integrated.
            if( context.CurrentDirectory != World.Name.WorldRoot )
            {
                monitor.Error( $"""
                                Discarding a branch only removes it from the branch model of the whole World. This must be run at the root of the World:
                                {World.Name.WorldRoot}
                                """ );
                return false;
            }
            return SaveBranchNamespace( monitor, _namespace.Remove( b ) );
        }
        var scope = World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        var all = World.GetAllDefinedRepo( monitor );
        if( scope == null || all == null ) return false;

        var infos = all.Select( r => Get( monitor, r ) ).ToArray();
        var toClose = CollectBranchesToClose( monitor, all, infos, scope, b, out bool success );
        if( !success ) return false;
        foreach( var hb in toClose )
        {
            // Only the closed branch and the branch that receives it matter: any other branch (and its issues)
            // is irrelevant here.
            var closest = hb.BranchModelInfo.GetRequiredClosestExistingBranch( monitor, b.Parent! );
            if( closest == null ) return false;
            if( hb.HasIssue() || closest.HasIssue() )
            {
                monitor.Error( $"Please fix the '{hb.BranchName.Name}' or '{closest.BranchName.Name}' branch issue in '{hb.Repo.DisplayPath}' before closing '{b.Name}'." );
                success = false;
            }
        }
        if( !success ) return false;
        foreach( var hb in toClose )
        {
            if( !hb.Close( monitor ) ) return false;
        }
        int stillOpened = infos.Count( i => i.Branches[b.Index].Exists );
        if( stillOpened > 0 )
        {
            monitor.Info( ScreenType.CKliScreenTag,
                          $"Branch '{b.Name}' closed in {Repositories( toClose.Count )}, still opened in {Repositories( stillOpened )}." );
            return true;
        }
        return SaveBranchNamespace( monitor, _namespace.Remove( b ) );
    }

    /// <summary>
    /// Collects the branches to close: the existing ones in the <paramref name="scope"/> and, transitively, the existing
    /// ones of the upstreams that produce a version of the branch consumed by a branch to close. Closing a downstream
    /// without them would integrate a reference to a version of the branch in the parent, and nothing would ever heal
    /// it since the upstream's parent never receives the changes that this version carries.
    /// </summary>
    List<HotBranch> CollectBranchesToClose( IActivityMonitor monitor,
                                            IReadOnlyList<Repo> all,
                                            BranchModelInfo[] infos,
                                            IReadOnlyList<Repo> scope,
                                            BranchName b,
                                            out bool success )
    {
        var reader = new SolutionReader( _shallowSolution );
        // The producers of the World where the branch exists, read from the branch: only these can be closed.
        var producers = new Dictionary<string, int>( System.StringComparer.OrdinalIgnoreCase );
        var solutions = new ShallowSolution.Plugin.GitSolution?[all.Count];
        for( int i = 0; i < all.Count; i++ )
        {
            var hb = infos[i].Branches[b.Index];
            if( !hb.Exists ) continue;
            var sol = reader.Read( monitor, all[i], hb.GitDevBranch ?? hb.GitBranch );
            if( sol != null )
            {
                solutions[i] = sol;
                RegisterProducer( producers, i, sol );
            }
        }
        var toClose = new List<HotBranch>();
        var closing = new bool[all.Count];
        var toProcess = new Queue<int>();
        foreach( var repo in scope )
        {
            int i = repo.Index;
            Throw.DebugAssert( all[i] == repo );
            if( infos[i].Branches[b.Index].Exists )
            {
                closing[i] = true;
                toProcess.Enqueue( i );
            }
        }
        if( toProcess.Count == 0 )
        {
            monitor.Info( ScreenType.CKliScreenTag, $"Branch '{b.Name}' doesn't exist in the {Repositories( scope.Count )} here." );
        }
        while( toProcess.TryDequeue( out var i ) )
        {
            toClose.Add( infos[i].Branches[b.Index] );
            var sol = solutions[i];
            if( sol == null ) continue;
            foreach( var c in sol.Consumed )
            {
                if( b.Match( c.Version )
                    && producers.TryGetValue( c.PackageId, out var u )
                    && !closing[u] )
                {
                    closing[u] = true;
                    toProcess.Enqueue( u );
                    monitor.Info( ScreenType.CKliScreenTag,
                                  $"Also closing '{b.Name}' in '{all[u].DisplayPath}': its '{b.Name}' versions are consumed by '{all[i].DisplayPath}'." );
                }
            }
        }
        success = reader.Success;
        return toClose;
    }
}
