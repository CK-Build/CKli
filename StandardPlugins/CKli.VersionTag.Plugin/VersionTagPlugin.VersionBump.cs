using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
    /// <summary>
    /// Bumps the current repository version number by setting a "+fake" version tag on the "&lt;root&gt;" or "dev/&lt;root&gt;" branch.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The CKli plugin context.</param>
    /// <param name="version">The bumped version.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """Bumps the current repository version number by setting a "+fake" version tag on the "<root>" or "dev/<root>" branch.""" )]
    [CommandPath( "version bump" )]
    public bool VersionBump( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description( "The new starting version. Must be a stable Major.Minor.Patch (no -prelease nor +metadata suffix) " +
                                           "greater than the greatest published version.")]
                             string version )
    {
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null ) return false;

        var futureFake = SVersion.ParseNoThrow( version );
        if( !futureFake.IsValid )
        {
            monitor.Error( futureFake.ErrorMessage );
            return false;
        }
        return BumpVersion( monitor, repo, context.Committer, futureFake );
    }

    /// <summary>
    /// Bumps a repository version number by setting a "+fake" version tag on a hot branch: the "&lt;root&gt;" or
    /// "dev/&lt;root&gt;" branch by default (this is the "version bump" command for any <paramref name="repo"/>), or the
    /// <paramref name="hotBranch"/> (its "dev/" branch when it exists).
    /// <para>
    /// The working folder is not touched: when the branch tip already carries another version, the tag is set on a new
    /// empty commit (same tree) that only moves the branch reference.
    /// </para>
    /// <para>
    /// On a branch other than the root, the fake is a branch fake (see <see cref="TagCommit.IsBranchFake"/>): it is the
    /// starting version of this branch only and reaches the root branch with the integration of the branch. It is
    /// registered in the <see cref="GitRepository.PersistentDeferredPushRefSpecs"/>: the next push shares it.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="committer">The committer of the empty commit that may be created.</param>
    /// <param name="futureFake">
    /// The new starting version. Must be a stable Major.Minor.Patch (no -prelease nor +metadata suffix) greater than the
    /// greatest published version.
    /// </param>
    /// <param name="hotBranch">The branch that receives the fake. Defaults to the root branch.</param>
    /// <returns>True on success, false on error.</returns>
    public bool BumpVersion( IActivityMonitor monitor, Repo repo, Signature committer, SVersion futureFake, HotBranch? hotBranch = null )
    {
        if( futureFake.IsPrerelease || futureFake.BuildMetaData.Length > 0 )
        {
            monitor.Error( """The version must be a stable Major.Minor.Patch (no -prelease nor +metadata suffix).""" );
            return false;
        }
        var branchInfo = _branchModel.Get( monitor, repo );
        if( branchInfo.Root.GitBranch == null )
        {
            monitor.Error( $"""The root branch '{branchInfo.Namespace.Root}' doesn't exist. Use 'ckli issue --fix' to fix this.""" );
            return false;
        }
        hotBranch ??= branchInfo.Root;
        bool isRoot = hotBranch.BranchName.IsRoot;
        var gitBranch = hotBranch.GitDevBranch ?? hotBranch.GitBranch;
        if( gitBranch == null )
        {
            monitor.Error( $"The branch '{hotBranch.BranchName.Name}' doesn't exist in '{repo.DisplayPath}'." );
            return false;
        }
        // Refreshed: the branch may have moved since the HotBranch has been obtained.
        var branch = repo.GitRepository.Repository.Branches[gitBranch.CanonicalName];
        
        var versionInfo = Get( monitor, repo );
        if( versionInfo.SupVersion != null && futureFake >= versionInfo.SupVersion )
        {
            monitor.Error( $"""Provided version must be lower than configured SupVersion="{versionInfo.SupVersion.ParsedText}".""" );
            return false;
        }
        if( versionInfo.InfVersion != null && futureFake <= versionInfo.InfVersion )
        {
            monitor.Error( $"""Provided version must be greater than configured InfVersion="{versionInfo.InfVersion.ParsedText}".""" );
            return false;
        }
        if( !versionInfo.CheckNoTagConflicts( monitor ) )
        {
            return false;
        }
        //
        // Ignore +fake (even if they are published by design).
        // What matters are only non fake published version (regular or deprecated). 
        var maxVersion = versionInfo.AllVersions.Select( tc => tc.Version ).Where( v => !v.HasFakeMetadata && !v.IsBuildingOrLocal() ).Max();
        if( futureFake <= maxVersion )
        {
            monitor.Error( $"""Provided version must be greater than the current maximal version "{maxVersion.ParsedText}".""" );
            return false;
        }

        // The fakes of the same scope (the root ones, or the branch ones that this branch contains) that are equal or
        // greater than the new version are replaced by it. A fake of another scope is left as is: a branch has its own
        // starting version. But two fakes of the same version can't coexist.
        var git = repo.GitRepository.Repository;
        var cleanupFake = versionInfo.AllVersions.Where( tc => tc.Version.HasFakeMetadata
                                                               && tc.Version >= futureFake
                                                               && (isRoot
                                                                    ? !tc.Commit.IsBranchFake
                                                                    : tc.Commit.IsBranchFake && VersionTagInfo.IsAncestorOrSelf( git, tc.Commit.Commit, branch.Tip )) )
                                                 .ToList();
        var sameVersion = versionInfo.AllVersions.FirstOrDefault( tc => tc.Version.HasFakeMetadata
                                                                        && tc.Version == futureFake
                                                                        && !cleanupFake.Any( f => f.Commit == tc.Commit ) );
        if( sameVersion.Commit != null )
        {
            monitor.Error( $"""
                The version '{sameVersion.Tag.FriendlyName}' is already the starting version of another branch (commit '{sameVersion.Commit.Sha.AsSpan( 0, 7 )}').
                Choose a greater version.
                """ );
            return false;
        }

        // We remove the "local/" versions (all of them for the root, the ones of the branch otherwise) but keep the
        // nuget cache. This is to preserve any current use of them (versions are removed from the cache each time they
        // are (re)built).
        bool success = versionInfo.DestroyLocalReleases( monitor,
                                                         filter: isRoot ? null : hotBranch.BranchName.Match,
                                                         removeFromNuGetGlobalCache: false );
        if( cleanupFake.Count > 0 )
        {
            success = DeleteFakeTags( monitor, repo, cleanupFake.Select( f => f.Tag.CanonicalName ).ToList() );
        }

        if( !success )
        {
            monitor.Warn( $"Error occurred but the 'v{futureFake}+fake' is nevertheless created on '{branch}'." );
        }

        // A commit produces at most one version: setting the "+fake" on a commit that already bears a
        // version with another Major.Minor.Patch would be a TagConflict.MultipleVersionsOnSameCommit (a
        // released version's future fake belongs elsewhere, and a commit carrying a "+fake" cannot have
        // produced another version). Since futureFake is necessarily greater than every published version,
        // the tip can only be SameStableAs it by carrying one of its prereleases: in every other case an
        // empty commit takes the fake.
        // A branch fake must be on a commit of its own: on a commit that the root branch contains (a branch without
        // commits of its own), it would be a root fake.
        // Note: RemoveFakeVersions above deleted git tags without updating this versionInfo (unlike
        // DestroyLocalReleases, which goes through RemoveTagCommit), so a just-removed fake can still be
        // reported here: cleanupFake filters it out to avoid a useless empty commit.
        var target = branch.Tip;
        bool tipBearsVersion = versionInfo.TagCommitsBySha.TryGetValue( target.Sha, out var onTip )
                               && !cleanupFake.Any( f => f.Commit == onTip )
                               && !futureFake.SameStableAs( onTip.Version );
        bool tipInRoot = !isRoot && IsInRootBranch( git, target );
        if( tipBearsVersion || tipInRoot )
        {
            var newTarget = CreateEmptyCommit( monitor,
                                               repo,
                                               committer,
                                               branch,
                                               $"Empty commit carrying the 'v{futureFake}+fake' version tag." );
            if( newTarget == null ) return false;
            monitor.Info( ScreenType.ScreenTag,
                          (tipBearsVersion
                            ? $"Commit '{target.Sha.AsSpan( 0, 7 )}' already carries 'v{onTip!.Version}': "
                            : $"Commit '{target.Sha.AsSpan( 0, 7 )}' is on the root branch: ")
                          + $"an empty commit has been created on '{branch}' to carry the new version tag." );
            target = newTarget;
        }
        // A branch fake is an annotated tag that records its branch: once the branch is abandoned (removed from the
        // branch model and its git branches deleted), the fake can be cleaned up (see AbandonedBranchFakesIssue).
        var fakeTag = isRoot
                        ? git.Tags.Add( $"v{futureFake}+fake", target, allowOverwrite: false )
                        : git.Tags.Add( $"v{futureFake}+fake", target, committer, TagCommit.CreateBranchFakeMessage( hotBranch.BranchName ), allowOverwrite: false );
        monitor.Info( ScreenType.ScreenTag, $"Tag 'v{futureFake}+fake' created on '{branch}'." );
        // A branch fake is pushed by the next push: the versions of the branch are based on it, the other clones
        // must see it to compute the same versions. (A root fake is pushed by the build that uses it.)
        if( !isRoot && !repo.GitRepository.AddPersistentDeferredPushRefSpecs( monitor, [$"+{fakeTag.CanonicalName}"] ) )
        {
            return false;
        }
        return true;

        // Creates an empty commit (same tree as the branch tip) on the branch and moves its ref, without
        // requiring the branch to be checked out and without touching the working folder: GitRepository.Commit
        // works on the checked out branch and stages "*", which would capture unrelated changes.
        static Commit? CreateEmptyCommit( IActivityMonitor monitor,
                                          Repo repo,
                                          Signature committer,
                                          Branch branch,
                                          string message )
        {
            try
            {
                var git = repo.GitRepository.Repository;
                var tip = branch.Tip;
                var newCommit = git.ObjectDatabase.CreateCommit( committer, committer, message, tip.Tree, [tip], prettifyMessage: true );
                git.Refs.UpdateTarget( branch.Reference, newCommit.Id, null );
                return newCommit;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While creating an empty commit on '{repo.DisplayPath}' branch '{branch.FriendlyName}'.", ex );
                return null;
            }
        }
    }

    // Deletes "+fake" tags. A "+invalid" can't cancel a "+fake": "vX+invalid" cancels every "vX" tag (the version
    // equality ignores the build metadata), the future released version included, and "vX+fake+invalid" is not a
    // conformant version (it would be ignored). So a fake that the remote has is deleted from the remote: the deletion
    // is a persistent deferred push (if this push fails, the next one does it). Note that a clone that has already
    // fetched it keeps it: fetches don't prune the tags.
    // A fake that has never been pushed (a branch fake registered in the persistent deferred push) is forgotten.
    static bool DeleteFakeTags( IActivityMonitor monitor, Repo repo, IReadOnlyList<string> canonicalNames )
    {
        var git = repo.GitRepository;
        bool success = git.RemovePersistentDeferredPushRefSpecs( monitor, canonicalNames.Select( n => $"+{n}" ) );
        if( !git.GetRemoteTags( monitor, out GitTagInfo? remoteTags ) )
        {
            return false;
        }
        using( monitor.OpenInfo( $"Deleting {canonicalNames.Count} +fake tags: {canonicalNames.Concatenate()}." ) )
        {
            var onRemote = canonicalNames.Where( n => remoteTags.IndexedTags.ContainsKey( n ) ).ToList();
            if( onRemote.Count > 0 )
            {
                monitor.Info( $"Deleting {onRemote.Count} tags from the remote: {onRemote.Concatenate()}." );
                success &= git.AddPersistentDeferredPushRefSpecs( monitor, onRemote.Select( n => $":{n}" ) )
                           && git.PushTags( monitor, [] );
            }
            return success && git.DeleteLocalTags( monitor, canonicalNames );
        }
    }
}
