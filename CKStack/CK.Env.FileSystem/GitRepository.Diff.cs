using CK.Core;
using CK.Env.Diff;

using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public partial class GitRepository : ICommandMethodsProvider
    {
        /// <summary>
        /// Gets the <see cref="GitDiffResult"/> for a set of roots from the provided commit up to current head.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="previousVersionCommitSha">Previous commit.</param>
        /// <param name="roots">Roots of interest. Typically projects of the solution.</param>
        /// <param name="withCommitMessages">True to get <see cref="GitDiffResult.Messages"/>.</param>
        /// <returns>The set of differences or null on error.</returns>
        public GitDiffResult? GetDiff( IActivityMonitor m, string previousVersionCommitSha, IEnumerable<GitDiffRoot> roots, bool withCommitMessages )
        {
            return GetDiff( m, previousVersionCommitSha, Head.CommitSha, roots, withCommitMessages );
        }

        /// <summary>
        /// Gets the <see cref="GitDiffResult"/> between two dates in the parent commits of the current head.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="from">Starting date.</param>
        /// <param name="to">Ending date.</param>
        /// <param name="roots">Roots of interest. Typically projects of the solution.</param>
        /// <param name="withCommitMessages">True to get <see cref="GitDiffResult.Messages"/>.</param>
        /// <returns>The set of differences or null on error.</returns>
        public GitDiffResult? GetDiff( IActivityMonitor m, DateTimeOffset from, DateTimeOffset to, IEnumerable<GitDiffRoot> roots, bool withCommitMessages )
        {
            Throw.CheckArgument( from < to );
            Commit? fromC = null;
            Commit? toC = null;
            foreach( var c in Git.Head.Commits.SkipWhile( p => p.Committer.When > to ) )
            {
                if( c.Committer.When < from ) break;
                if( toC == null ) toC = c;
                fromC = c;
            }
            return fromC == null || fromC == toC
                    ? CreateEmptyDiffResult( roots )
                    : GetDiff( m, roots, fromC, toC!, withCommitMessages );
        }

        /// <summary>
        /// Gets all the commit messages between two dates.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="from">Starting date.</param>
        /// <param name="to">Ending date.</param>
        /// <returns>The messages.</returns>
        public IReadOnlyList<CommitMessage> GetCommitMessagesBetween( IActivityMonitor m, DateTimeOffset from, DateTimeOffset to )
        {
            Throw.CheckArgument( from < to );
            var all = Git.Commits.Where( p => p.Committer.Name != "CKli" && p.Committer.When >= from && p.Committer.When <= to )
                                 .OrderBy( p => p.Committer.When )
                                 .Select( p => new CommitMessage( p.Id.Sha, p.Committer.When, p.Committer.Name, p.Message ) );
            return all.ToArray();
        }

        /// <summary>
        /// Gets the <see cref="GitDiffResult"/> for a set of roots between two commits.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="fromCommitSha">Starting commit.</param>
        /// <param name="toCommitSha">Target commit.</param>
        /// <param name="roots">Roots of interest. Typically projects of the solution.</param>
        /// <param name="withCommitMessages">True to get <see cref="GitDiffResult.Messages"/>.</param>
        /// <returns>The set of differences or null on error.</returns>
        public GitDiffResult? GetDiff( IActivityMonitor m, string fromCommitSha, string toCommitSha, IEnumerable<GitDiffRoot> roots, bool withCommitMessages )
        {
            Commit? from = Find( m, fromCommitSha, nameof( fromCommitSha ) );
            Commit? to = Find( m, toCommitSha, nameof( toCommitSha ) );
            if( from == null || to == null ) return null;
            return GetDiff( m, roots, from, to, withCommitMessages );
        }

        /// <summary>
        /// Gets the <see cref="GitDiffResult"/> for a set of roots between two commits.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="from">Starting commit.</param>
        /// <param name="to">Target commit.</param>
        /// <param name="roots">Roots of interest. Typically projects of the solution.</param>
        /// <param name="withCommitMessages">True to get <see cref="GitDiffResult.Messages"/>.</param>
        /// <returns>The set of differences or null on error.</returns>
        public GitDiffResult GetDiff( IActivityMonitor monitor, IEnumerable<GitDiffRoot> roots, Commit from, Commit to, bool withCommitMessages )
        {
            Throw.CheckNotNullArgument( from );
            Throw.CheckNotNullArgument( to );

            if( from.Id == to.Id )
            {
                return CreateEmptyDiffResult( roots );
            }
            var alienRoot = new GitDiffRoot( "Others", Array.Empty<NormalizedPath>() );
            var alien = new DiffRootResultBuilderOther( alienRoot );
            DiffResultBuilder builder = new DiffResultBuilder( Git,
                                                               roots.Select( r => new DiffRootResultBuilder( r ) ).ToList(), alien );
            return builder.BuildDiffResult( monitor, from, to, withCommitMessages );
        }

        Commit? Find( IActivityMonitor monitor, string sha, string parameterName )
        {
            Commit? commit = Git.Lookup<Commit>( sha );
            if( commit == null ) monitor.Error( $"Unable to find {parameterName}: {sha}." );
            return commit;
        }

        static GitDiffResult CreateEmptyDiffResult( IEnumerable<GitDiffRoot> roots )
        {
            return new GitDiffResult( roots.Select( r => new GitDiffRootResult( r ) ).ToArray(), new GitDiffRootResult( new GitDiffRoot( "Others", Array.Empty<NormalizedPath>() ) ), null );
        }
    }
}
