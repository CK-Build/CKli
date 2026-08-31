using CK.Core;
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
        var branch = branchInfo.Root.GitDevBranch ?? branchInfo.Root.GitBranch;
        
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

        // We remove all the "local/" versions but keep the nuget cache.
        // This is to preserve anu current use of them (versions are removed from the cache each time they are (re)built).
        bool success = versionInfo.DestroyLocalReleases( monitor, filter: null, removeFromNuGetGlobalCache: false );

        // We remove all the fake versions that are equal or greater to the new version.
        var cleanupFake = versionInfo.AllVersions.Where( tc => tc.Version.HasFakeMetadata && tc.Version >= futureFake ).ToList();
        if( cleanupFake.Count > 0 )
        {
            bool pushInvalidTags = true;
            success = RemoveFakeVersions( monitor, repo, cleanupFake, pushInvalidTags );
        }

        if( !success )
        {
            monitor.Warn( $"Error occurred but the 'v{futureFake}+invalid' is nevertheless created on '{branch}'." );
        }

        // A commit produces at most one version: setting the "+fake" on a commit that already bears a
        // version it is not based on would be a TagConflict.MultipleVersionsOnSameCommit (a released
        // version's future fake belongs elsewhere, and a commit carrying a "+fake" cannot have produced an
        // unrelated version). Since futureFake is necessarily greater than every published version, it can
        // never be a IsStableRoughBaseOf what the tip already carries: an empty commit takes the fake.
        // Note: RemoveFakeVersions above deleted git tags without updating this versionInfo (unlike
        // DestroyLocalReleases, which goes through RemoveTagCommit), so a just-removed fake can still be
        // reported here: cleanupFake filters it out to avoid a useless empty commit.
        var target = branch.Tip;
        if( versionInfo.TagCommitsBySha.TryGetValue( target.Sha, out var onTip )
            && !cleanupFake.Any( f => f.Commit == onTip )
            && !futureFake.IsStableRoughBaseOf( onTip.Version ) )
        {
            var newTarget = CreateEmptyCommit( monitor,
                                               repo,
                                               context.Committer,
                                               branch,
                                               $"Empty commit carrying the 'v{futureFake}+fake' version tag." );
            if( newTarget == null ) return false;
            monitor.Info( ScreenType.CKliScreenTag,
                          $"Commit '{target.Sha.AsSpan( 0, 7 )}' already carries 'v{onTip.Version}': "
                          + $"an empty commit has been created on '{branch}' to carry the new version tag." );
            target = newTarget;
        }
        repo.GitRepository.Repository.Tags.Add( $"v{futureFake}+fake", target, allowOverwrite: false );
        monitor.Info( ScreenType.CKliScreenTag, $"Tag 'v{futureFake}+fake' created on '{branch}'." );
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

        static bool RemoveFakeVersions( IActivityMonitor monitor,
                                        Repo repo,
                                        List<(SVersion Version, Tag Tag, TagCommit Commit)> cleanupFake,
                                        bool pushInvalidTags )
        {
            bool success = true;
            if( !repo.GitRepository.GetRemoteTags( monitor, out GitTagInfo? remoteTags ) )
            {
                success = false;
            }
            else
            {
                using( monitor.OpenInfo( $"Removing {cleanupFake.Count} +fake versions." ) )
                {
                    List<string>? tagToDelete = null;
                    List<TagInfo>? tagToInvalid = null;
                    foreach( var fake in cleanupFake )
                    {
                        var tagName = fake.Tag.CanonicalName;
                        if( remoteTags.IndexedTags.TryGetValue( tagName, out var tagInfo ) )
                        {
                            tagToInvalid ??= new List<TagInfo>();
                            tagToInvalid.Add( tagInfo );
                        }
                        else
                        {
                            tagToDelete ??= new List<string>();
                            tagToDelete.Add( tagName );
                        }
                    }
                    if( tagToDelete != null )
                    {
                        monitor.Info( $"Deleting {tagToDelete.Count} local tags: {tagToDelete.Concatenate()}." );
                        success &= repo.GitRepository.DeleteLocalTags( monitor, tagToDelete );
                    }
                    if( tagToInvalid != null )
                    {
                        using( monitor.OpenInfo( $"Creating {tagToInvalid.Count} +invalid tags." ) )
                        {
                            var names = new List<string>();
                            foreach( var i in tagToInvalid )
                            {
                                var name = i.CanonicalName + "+invalid";
                                // The +invalid tag must not exist otherwise we won't have found the version.
                                // => Use allowOverwrite: false.
                                repo.GitRepository.Repository.Tags.Add( name, i.Commit, allowOverwrite: false );
                                names.Add( name );
                            }
                            if( pushInvalidTags )
                            {
                                success &= repo.GitRepository.PushTags( monitor, names );
                            }
                            success &= repo.GitRepository.DeleteLocalTags( monitor, tagToInvalid.Select( i => i.CanonicalName ) );
                        }
                    }
                }
            }

            return success;
        }
    }


}
