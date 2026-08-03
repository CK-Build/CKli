using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Generic;
using System.Linq;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
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
        //
        // Ignore +fake (even if they are published by design).
        // What matters are only non fake published version (regular or deprecated). 
        var maxVersion = versionInfo.AllVersions.Select( tc => tc.Version ).Where( v => !v.HasFakeMetadata && !v.IsLocal() ).Max();
        if( futureFake <= maxVersion )
        {
            monitor.Error( $"""Provided version must be greater than the current maximal version "{maxVersion.ParsedText}".""" );
            return false;
        }
        bool success = true;
        // We remove all the "local/" versions.
        var cleanupLocals = versionInfo.AllVersions.Select( tc => tc.Version ).Where( v => v.IsLocal() ).ToList();
        if( cleanupLocals.Count > 0 )
        {
            using( monitor.OpenInfo( $"""Destroying {cleanupLocals.Count} "local/" versions: {cleanupLocals.Select( v => v.ParsedText ).Concatenate()}.""" ) )
            {
                foreach( var local in cleanupLocals )
                {
                    success &= DestroyLocalRelease( monitor, repo, local );
                }
            }
        }
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
        repo.GitRepository.Repository.Tags.Add( $"v{futureFake}+invalid", branch.Tip, allowOverwrite: false );
        return true;

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
