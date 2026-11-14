using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
    // A branch fake is abandoned when its branch is gone: the branch it records is not in the branch model anymore
    // ("branch close --discard") and no git branch has its name or the name of its "dev/" branch, locally or on the
    // remote. A branch fake that doesn't record its branch (a lightweight one, created by hand) is never considered
    // abandoned: another clone may have its branch.
    // An abandoned branch fake is removed only when no published version is based on it (a version produced on a
    // commit that contains it, with its Major.Minor.Patch): it is what makes these versions understandable. It is
    // then ignored (no tree contains it) and kept.
    void CollectAbandonedBranchFakesIssue( IActivityMonitor monitor, VersionTagInfo info, ScreenType screenType, Action<World.Issue> collector )
    {
        if( info.BranchFakes.Count == 0 || info.HasTagConflicts ) return;
        var git = info.Repo.GitRepository.Repository;
        List<(TagCommit Fake, List<SVersion> LocalVersions)>? abandoned = null;
        foreach( var fake in info.BranchFakes )
        {
            var name = fake.RecordedBranchName;
            if( name == null
                || _branchModel.BranchNamespace.Find( name ) != null
                || BranchExists( git, name )
                || BranchExists( git, fake.RecordedDevBranchName ) )
            {
                continue;
            }
            var based = info.AllTagCommits.Where( tc => !tc.IsFakeVersion
                                                        && tc.Version.SameStableAs( fake.Version )
                                                        && VersionTagInfo.IsAncestorOrSelf( git, fake.Commit, tc.Commit ) )
                                          .ToList();
            var published = based.Where( tc => !tc.IsBuildingOrLocal ).ToList();
            if( published.Count > 0 )
            {
                monitor.Info( $"""
                    The branch '{name}' of '{fake.Tag.FriendlyName}' in '{info.Repo.DisplayPath}' doesn't exist anymore, but published versions are based on it:
                    {published.Select( tc => tc.Version.ParsedText ).Concatenate()}. It is kept.
                    """ );
                continue;
            }
            abandoned ??= new List<(TagCommit, List<SVersion>)>();
            abandoned.Add( (fake, based.Select( tc => tc.Version ).ToList()) );
        }
        if( abandoned != null )
        {
            collector( new AbandonedBranchFakesIssue( info, abandoned, screenType ) );
        }

        static bool BranchExists( Repository git, string? name )
        {
            return name != null && (git.Branches[name] != null || git.Branches[$"origin/{name}"] != null);
        }
    }

    sealed class AbandonedBranchFakesIssue : World.Issue
    {
        readonly VersionTagInfo _info;
        readonly List<(TagCommit Fake, List<SVersion> LocalVersions)> _abandoned;

        public AbandonedBranchFakesIssue( VersionTagInfo info,
                                          List<(TagCommit Fake, List<SVersion> LocalVersions)> abandoned,
                                          ScreenType screenType )
            : base( $"Found {abandoned.Count} abandoned branch fake versions.",
                    screenType.Text( $"""
                        {abandoned.Select( a => $"- '{a.Fake.Tag.FriendlyName}' of branch '{a.Fake.RecordedBranchName}'"
                                                + (a.LocalVersions.Count > 0
                                                    ? $" (local versions: {a.LocalVersions.Select( v => v.ParsedText ).Concatenate()})."
                                                    : ".") ).Concatenate( Environment.NewLine )}
                        Their branches don't exist anymore and no published version is based on them.
                        They will be removed with their local versions (and deleted from the remote when it has them).
                        """ ),
                    info.Repo,
                    implicitIssue: false )
        {
            _info = info;
            _abandoned = abandoned;
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world, CancellationToken cancellation )
        {
            Throw.DebugAssert( Repo != null );
            bool success = true;
            using( monitor.OpenInfo( $"Removing {_abandoned.Count} abandoned branch fake versions in '{Repo.DisplayPath}'." ) )
            {
                foreach( var (_, locals) in _abandoned )
                {
                    foreach( var v in locals )
                    {
                        success &= _info.DestroyLocalRelease( monitor, v );
                    }
                }
                success &= DeleteFakeTags( monitor, Repo, _abandoned.Select( a => a.Fake.Tag.CanonicalName ).ToList() );
            }
            return ValueTask.FromResult( success );
        }
    }
}
