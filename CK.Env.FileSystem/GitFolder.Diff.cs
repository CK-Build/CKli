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
            Commit commit = _git.Lookup<Commit>( previousVersionCommitSha );
            var commits = _git.Commits.QueryBy( new CommitFilter()
            {
                IncludeReachableFrom = _git.Head.Tip,
                ExcludeReachableFrom = commit
            } );
            return GetReleaseDiff( m, roots, commits.ToList() );
        }

        /// <summary>
        /// Create Diff on a single file.
        /// </summary>
        /// <param name="topCommit"></param>
        /// <param name="commit"></param>
        /// <param name="diffName"></param>
        /// 
        /// <returns></returns>
        internal DiffRootResult CreateDiff( IActivityMonitor m, IDiffRoot root, List<Commit> commits, Func<TreeEntryChanges, bool> includeInDiffPredicate )
        {
            var commitsAndParents = commits.Select( s => (s, s.Parents.FirstOrDefault()) );
            //There is a diff with the first commit and his parent, and we don't want it
            var commitsAndDiffWithParent = commitsAndParents.Select( c => (c.s, _git.Diff.Compare<TreeChanges>( c.s.Tree, c.Item2.Tree )) );
            List<Commit> commitsThatChangedTheDirectory = commitsAndDiffWithParent.Where( p => p.Item2.Any( includeInDiffPredicate ) ).Select( p => p.s ).ToList();
            m.Debug( $"Found {commitsThatChangedTheDirectory.Count} commits that satisfy the predicate." );
            using( TreeChanges changes = _git.Diff.Compare<TreeChanges>( commits.Last().Tree, commits.First().Tree ) )
            {
                List<FileReleaseDiff> allChanges = changes.Where( includeInDiffPredicate ).Select( gC => new FileReleaseDiff( gC.Path, (FileReleaseDiffType)gC.Status ) ).ToList();
                m.Debug( $"Found {allChanges.Count} file or directory changes that satisfy the predicate." );
                return new DiffRootResult( root, allChanges, commitsThatChangedTheDirectory.Select( p => new CommitInfo( p.Message, p.Sha ) ).ToList() );
            }
        }

        DiffResult GetReleaseDiff( IActivityMonitor m, IEnumerable<IDiffRoot> roots, List<Commit> commits )
        {
            var results = new List<DiffRootResult>();
            foreach( var root in roots )
            {
                results.Add( CreateDiff( m, root, commits, changes => root.Paths.Any( path => changes.Path.StartsWith( path ) ) ) );
            }
            var other = CreateDiff( m, new DiffRoot( "Other changes", new List<NormalizedPath>() ), commits, changes => !roots.Any( root => root.Paths.Any( path => changes.Path.StartsWith( path ) ) ) );
            return new DiffResult( true, results, other );
        }

        IEnumerable<Commit> GetCommitsBetweenDates( DateTimeOffset beginning, DateTimeOffset ending )
        {
            if( ending < beginning ) throw new ArgumentException( $"{nameof( ending )}<{nameof( beginning )}" );
            return _git.Head.Commits.SkipWhile( p => p.Committer.When > ending ).TakeWhile( p => p.Committer.When > beginning );
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
