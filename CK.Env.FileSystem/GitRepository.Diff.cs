using CK.Core;
using CK.Env.Diff;

using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public partial class GitRepository : IGitHeadInfo, ICommandMethodsProvider
    {
        /// <summary>
        /// Gets the <see cref="DiffResult"/> for a set of roots from the provided commit up to current head.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="previousVersionCommitSha">Previous commit.</param>
        /// <param name="roots">Roots of interest. Typically projects of the solution.</param>
        /// <returns>The set of differences or null on error.</returns>
        public DiffResult? GetDiff( IActivityMonitor m, string previousVersionCommitSha, IEnumerable<DiffRoot> roots )
        {
            Commit? commit = Git.Lookup<Commit>( previousVersionCommitSha );
            if( commit == null )
            {
                m.Error( $"Unable to find previousVersionCommitSha:{previousVersionCommitSha}." );
                return null;
            }
            var alienRoot = new DiffRoot( "Others", Array.Empty<NormalizedPath>() );
            if( commit.Id == Git.Head.Tip.Id )
            {
                return new DiffResult( roots.Select( r => new DiffRootResult( r ) ).ToArray(), new DiffRootResult( alienRoot ) );
            }
            var alien = new DiffRootResultBuilderOther( alienRoot );
            DiffResultBuilder builder = new DiffResultBuilder( Git,
                                                               roots.Select( r => new DiffRootResultBuilder( r ) ).ToList(), alien );
            return builder.BuildDiffResult( m, commit, Git.Head.Tip );
        }
    }
}
