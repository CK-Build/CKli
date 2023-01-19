using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Diff
{
    sealed class DiffResultBuilder
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

        public GitDiffResult BuildDiffResult( IActivityMonitor monitor, Commit from, Commit to, bool withCommitMessages )
        {
            var fullDiff = _git.Diff.Compare<TreeChanges>( from.Tree, to.Tree );
            IReadOnlyList<CommitMessage>? messages = null;
            using( monitor.OpenDebug( $"Diffing between '{from.Id.ToString( 7 )} {from.MessageShort}' and '{to.Id.ToString( 7 )} {to.MessageShort}'." ) )
            {
                using( monitor.OpenDebug( "Caching changes" ) )
                {
                    foreach( TreeEntryChanges change in fullDiff )
                    {
                        switch( change.Status )
                        {
                            case ChangeKind.Unmodified:
                                continue;
                            case ChangeKind.Added:
                                RunOnBuilders( monitor, new AddedDiff( change.Path ) );
                                break;
                            case ChangeKind.Deleted:
                                RunOnBuilders( monitor, new DeletedDiff( change.Path ) );
                                break;
                            case ChangeKind.Renamed:
                            case ChangeKind.Modified:
                                RunOnBuilders( monitor, new ModifiedDiff( change.OldPath, change.Path ) );
                                break;
                            default:
                                monitor.Warn( $"Unhandled diff change: Path = {(!string.IsNullOrEmpty( change.Path ) ? change.Path : change.OldPath)}, Status {change.Status}." );
                                break;
                        }
                    }
                }
                if( withCommitMessages )
                {
                    var logs = _git.Commits.QueryBy( new CommitFilter() { IncludeReachableFrom = to, ExcludeReachableFrom = from.Parents, SortBy = CommitSortStrategies.Time|CommitSortStrategies.Reverse } );
                    messages = logs.Where( c => c.Committer.Name != "CKli" )
                                   .Select( c => new CommitMessage(c.Sha, c.Committer.When, c.Committer.Name, c.Message ) )
                                   .ToArray();
                }
            }
            return new GitDiffResult( _diffRootResultBuilders.Select( p => p.Result ).ToList(), _others.Result, messages );
        }
    }
}
