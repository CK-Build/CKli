using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.Diff
{
    class DiffResultBuilder
    {
        readonly Repository _git;
        readonly IReadOnlyList<DiffRootResultBuilderBase> _diffRootResultBuilders;
        readonly DiffRootResultBuilderBase _others;

        internal DiffResultBuilder( Repository git, IReadOnlyList<DiffRootResultBuilderBase> diffRootResultBuilders, DiffRootResultBuilderBase other )
        {
            _git = git;
            _diffRootResultBuilders = diffRootResultBuilders;
            _others = other;
        }

        void RunOnBuilders( IActivityMonitor m, IDiff diff )
        {
            bool accepted = false;
            foreach( var builder in _diffRootResultBuilders )
            {
                if( diff.SendToBuilder( m, builder ) ) accepted = true;
            }
            if( !accepted ) diff.SendToBuilder( m, _others );
        }

        public DiffResult BuildDiffResult( IActivityMonitor m, List<Commit> commits )
        {
            var commitsAndParents = commits.Select( s => (s, s.Parents.FirstOrDefault()) );
            var commitWithDiff = commitsAndParents.Select( c => (c.s, _git.Diff.Compare<TreeChanges>( c.s.Tree, c.Item2.Tree )) ).ToList();
            Commit firstCommit = commits.First();
            Commit lastCommit = commits.Last();
            if( firstCommit.Committer.When < lastCommit.Committer.When )
            {
                throw new ArgumentException( "Unchronological commits order." );
            }
            m.Debug( $"Diffing between {firstCommit.Sha} and {lastCommit.Sha}" );
            var fullDiff = _git.Diff.Compare<TreeChanges>( firstCommit.Tree, lastCommit.Tree );

            using( m.OpenDebug( "Finding commits that impacted changes." ) )
            {
                using( m.OpenDebug( "Caching changes" ) )
                {
                    foreach( TreeEntryChanges change in fullDiff )
                    {
                        switch( change.Status )
                        {
                            case ChangeKind.Unmodified:
                                m.Debug( $"Skipping {change.Path} because: {change.Status}." );
                                continue;
                            case ChangeKind.Added:
                                RunOnBuilders( m, new AddedDiff( change.Path ) );
                                break;
                            case ChangeKind.Deleted:
                                RunOnBuilders( m, new DeletedDiff( change.Path ) );
                                break;
                            case ChangeKind.Renamed:
                            case ChangeKind.Modified:
                                RunOnBuilders( m, new ModifiedDiff( change.OldPath, change.Path ) );
                                break;
                            default:
                                throw new NotImplementedException();//So it's actually used... i tought it would not.
                        }
                    }
                }
            }
            //all the commits are now cached.
            return new DiffResult( true, _diffRootResultBuilders.Select( p => p.Result ).ToList(), _others.Result );
        }
    }
}
