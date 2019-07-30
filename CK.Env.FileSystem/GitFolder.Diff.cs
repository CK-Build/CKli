using CK.Core;
using CK.Env.Diff;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public partial class GitFolder : IGitRepository, IGitHeadInfo, ICommandMethodsProvider
    {
        /// <summary>
        /// Gets the set of <see cref="DiffRootResult"/> for packages from the current head.
        /// </summary>
        /// <param name="m">The minitor to use.</param>
        /// <param name="previousVersionCommitSha">Previous commit.</param>
        /// <param name="roots">Generated packages of a solution.</param>
        /// <returns>The set of diff or null on error.</returns>
        public IDiffResult GetDiff( IActivityMonitor m, string previousVersionCommitSha, IEnumerable<IDiffRoot> roots )
        {
            Commit commit = Git.Lookup<Commit>( previousVersionCommitSha );
            var commits = Git.Commits.QueryBy( new CommitFilter()
            {
                IncludeReachableFrom = Git.Head.Tip,
                ExcludeReachableFrom = commit
            } );
            return GetReleaseDiff( m, roots, commits.ToList() );
        }

        DiffResult GetReleaseDiff( IActivityMonitor m, IEnumerable<IDiffRoot> roots, List<Commit> commits )
        {
            DiffResultBuilder builder = new DiffResultBuilder(
                Git,
                roots.Select(
                    r => new DiffRootResultBuilder( r ) ).ToList(),
                    new DiffRootResultBuilderOther(new DiffRoot("Others", new List<NormalizedPath>()) )
            );
            return builder.BuildDiffResult( m, commits );
        }

        IEnumerable<Commit> GetCommitsBetweenDates( DateTimeOffset beginning, DateTimeOffset ending )
        {
            if( ending < beginning ) throw new ArgumentException( $"{nameof( ending )}<{nameof( beginning )}" );
            return Git.Head.Commits.SkipWhile( p => p.Committer.When > ending ).TakeWhile( p => p.Committer.When > beginning );
        }

        public void ShowLogsBetweenDates(
            IActivityMonitor m,
            DateTimeOffset beginning,
            DateTimeOffset ending,
            IEnumerable<DiffRoot> diffRoot )
        {
            List<Commit> commits = GetCommitsBetweenDates( beginning, ending ).ToList();
            if( commits.Count == 0 )
            {
                m.Info( "No commits between the given dates." );
            }
            else
            {
                IDiffResult diffResult = GetReleaseDiff( m, diffRoot, commits );
                Console.WriteLine( diffResult.ToString() );
            }
        }
    }
}
