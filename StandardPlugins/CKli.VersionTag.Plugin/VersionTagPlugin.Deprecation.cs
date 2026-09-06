using CK.Core;
using CK.PerfectEvent;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
    // The sender lives with the command that raises it.
    readonly PerfectEventSender<VersionDeprecatedEventArgs> _versionDeprecated = new();

    /// <summary>
    /// Raised by "ckli version deprecate" once every "+deprecated" tag it implies has been created or
    /// updated and pushed: the deprecation is public when this is raised.
    /// <para>
    /// Each listener chooses its own handler kind - <see cref="PerfectEvent{T}.Sync"/>,
    /// <see cref="PerfectEvent{T}.Async"/> or <see cref="PerfectEvent{T}.ParallelAsync"/>. This is why
    /// <see cref="DeprecateVersion"/> is asynchronous: raising a PerfectEvent is always awaited.
    /// </para>
    /// <para>
    /// A handler that throws fails the command. Re-running the deprecation is harmless, so failing is the
    /// honest outcome: the tags are pushed but whatever mirrors them is not up to date.
    /// </para>
    /// </summary>
    public PerfectEvent<VersionDeprecatedEventArgs> VersionDeprecated => _versionDeprecated.PerfectEvent;

    /// <summary>
    /// Deprecates the specified version.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="version"></param>
    /// <param name="reason"></param>
    /// <param name="days"></param>
    /// <param name="immediate"></param>
    /// <param name="allowUpdate"></param>
    /// <returns></returns>
    [Description( """
        Deprecates a version tag by ensuring that an associated "+deprecated" tag appears on the same commit and propagates this deprecation to all its downstream packages.
        The tag annotation contains the actual "Expiration" date at which the packages must be unlisted or deleted from any feeds: this must be set thanks to the --immediate flag or --days option.
        """ )]
    [CommandPath( "version deprecate" )]
    public async Task<bool> DeprecateVersion( IActivityMonitor monitor,
                                              CKliEnv context,
                                              [Description("The version to deprecate.")]
                                              string version,
                                              [Description("""Appear in the tag annotation. Defaults to "(unspecified)".""")]
                                              string? reason = null,
                                              [Description("Specify the actual deprecation delay in days. This option excludes --immediate.")]
                                              string? days = null,
                                              [Description("Apply the deprecation immediately. This flag excludes --days.")]
                                              bool immediate = false,
                                              [Description("Allow the deprecated tag to already exist and updates it (must not already be expired).")]
                                              bool allowUpdate = false )
    {
        // Before the actual deprecation that requires no version tag issue on any repository (because deprecation can touch multiple repositories),
        // we check that the version exists and is not a "local/" one.
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null )
        {
            return false;
        }
        if( !SVersion.TryParse( version, out var v ) )
        {
            monitor.Error( $"Unable to parse version '{version}': {v.ErrorMessage}" );
            return false;
        }
        var info = Get( monitor, repo );
        if( !info.TryGetTagCommit( v, out var tagCommit ) )
        {
            monitor.Error( $"Unable to find version tag 'v{version}'." );
            return false;
        }
        if( tagCommit.Version.IsBuildingOrLocal() )
        {
            monitor.Error( $"Version 'v{version}' has not been published yet." );
            return false;
        }
        if( tagCommit.IsFakeVersion )
        {
            monitor.Error( $"""
                Version 'v{v}' is a '+fake' version.
                You can use 'ckli tag delete' to remove the tag if needed.
                """ );
            return false;
        }
        // The alive version is the one the consumers use today. Deprecating it would leave this repository
        // with nothing usable at all, so the replacement must be published first: once it is, this version
        // is no longer the alive one and this deprecation is allowed.
        //
        // This only guards the CREATION of a deprecation. A deprecation is irreversible - only its "when"
        // can be changed - so an alive version that already carries a "+deprecated" tag must remain
        // updatable. Both this guard and the way "ckli build" rebuilds a deprecated version make that state
        // unreachable, but if a manual tag edit produces it, the expiration must still be fixable.
        if( !tagCommit.IsDeprecatedVersion && info.AliveStable == tagCommit )
        {
            monitor.Error( $"""
                Version 'v{v}' is the current version of '{repo.DisplayPath}': it cannot be deprecated.
                Publish a newer version first, then deprecate 'v{v}'.
                """ );
            return false;
        }

        // Now, lets start by building the version database.
        var releaseDatabase = EnsureDatabase( monitor );
        if( releaseDatabase == null )
        {
            return false;
        }
        // Now, we know that there's no issue on our starting repository.
        Throw.DebugAssert( !info.HasIssue );
        int daysDelay = -1;
        if( immediate )
        {
            if( days != null )
            {
                monitor.Error( "Flag --immediate and option --days cannot be specified at the same time." );
                return false;
            }
            daysDelay = 0;
        }
        else if( days != null )
        {
            if( !int.TryParse( days, out daysDelay ) || daysDelay <= 0 )
            {
                monitor.Error( "Invalid --days. Must be a positive integer." );
                return false;
            }
        }
        // Ensures that:
        //  - The +deprecated tag exists in this repo (creates or updates it).
        //  - And that it appears in the DeferredPushRefSpec ("+refs/tags/...").
        //  - And if HasExpired, the deprecated tag version appears in the DeferredPushRefSpec (in order to remove it ":refs/tags/...").
        DeprecatedTagInfo? tagInfo = EnsureRootDeprecatedTag( monitor, tagCommit, reason, daysDelay, allowUpdate );
        if( tagInfo == null )
        {
            return false;
        }
        var releaseInfo = releaseDatabase.GetReleaseInfo( monitor, tagCommit, v.CINumber == 0 );
        var visited = new HashSet<RepoReleaseInfo>() { releaseInfo };
        // "visited" is what the propagation reached: a release with no version tag left, or a "+fake" one,
        // is added to it but is NOT deprecated (the propagation stops there with a warning). Only the
        // releases that actually carry a "+deprecated" tag can be mirrored, hence this second collection.
        var deprecated = new List<RepoReleaseInfo>() { releaseInfo };
        EnsureImpliedDeprecatedTag( monitor, releaseInfo, visited, deprecated, path: [releaseInfo], tagInfo.DaysDelay, tagInfo.Expiration );

        bool success = true;
        using( monitor.OpenInfo( $"Pushing tags creation (and suppression if any) to remote origin repositories." ) )
        {
            foreach( var r in visited )
            {
                success &= r.Repo.GitRepository.PushTags( monitor, [] );
            }
        }
        // The deprecation is now public: whatever mirrors it can be updated.
        var e = new VersionDeprecatedEventArgs( monitor, context, World, releaseInfo, tagInfo, [.. deprecated] );
        if( !await _versionDeprecated.SafeRaiseAsync( monitor, e ).ConfigureAwait( false ) )
        {
            success = false;
        }
        return success;
    }

    internal static DeprecatedTagInfo? EnsureRootDeprecatedTag( IActivityMonitor monitor,
                                                                TagCommit existing,
                                                                string? reason,
                                                                int daysDelay,
                                                                bool allowUpdate )
    {
        var tagInfo = existing.DeprecatedInfo;
        if( tagInfo == null )
        {
            if( daysDelay == -1 )
            {
                monitor.Error( "To create a new deprecation tag, flag --immediate or option --days must be specified." );
                return null;
            }
            return CreateDeprecationTag( monitor, existing, reason, daysDelay );
        }
        if( tagInfo.HasExpired )
        {
            monitor.Warn( $"""
            Version 'v{existing.Version}' has already expired:
            {existing.TagMessage}

            """ );
            // We return the expired tagInfo.
            return tagInfo;
        }
        // Updated +deprecated tag.
        if( !allowUpdate )
        {
            monitor.Error( $"""
                This version is already 'v{existing.Version}':
                {existing.TagMessage}

                Use --allow-update to update it.
                """ );
            return null;
        }
        // Normalize empty reason to null.
        reason = string.IsNullOrWhiteSpace( reason ) ? null : reason;
        if( daysDelay == -1 && reason == null )
        {
            monitor.Error( $"""
                To update 'v{existing.Version}', at least --immediate, --days and/or --reason must be specified.
                """ );
            return null;
        }
        var newExpiration = daysDelay != -1
                                ? DateOnly.FromDateTime( DateTime.UtcNow.AddDays( daysDelay ) )
                                : tagInfo.Expiration;

        return newExpiration == tagInfo.Expiration && (reason == null || reason == tagInfo.Reason)
                ? tagInfo
                : UpdateExistingDeprecationTag( monitor, existing, tagInfo, reason, daysDelay, newExpiration );
    }

    // This is used by UpdateExistingDeprecationTag and CreateDeprecationTag: this pushes the
    // tag creation/update to the origin remote.
    static void AddTag( Repo repo, TagCommit existing, DeprecatedTagInfo tagInfo, string name )
    {
        repo.GitRepository.Repository.Tags.Add( name,
                                                existing.Commit,
                                                repo.GitRepository.Committer,
                                                tagInfo.ToString(),
                                                allowOverwrite: true );
        repo.GitRepository.DeferredPushRefSpecs.Add( $"+refs/tags/{name}" );
    }

    static DeprecatedTagInfo UpdateExistingDeprecationTag( IActivityMonitor monitor,
                                                           TagCommit existingCommit,
                                                           DeprecatedTagInfo existingTagInfo,
                                                           string? reason,
                                                           int daysDelay,
                                                           DateOnly expiration )
    {
        existingTagInfo = new DeprecatedTagInfo( existingTagInfo.ContentInfo,
                                                 expiration,
                                                 daysDelay != -1 ? daysDelay : existingTagInfo.DaysDelay,
                                                 reason ?? existingTagInfo.Reason );

        var name = existingCommit.Tag.FriendlyName;
        var repo = existingCommit.Repo;
        AddTag( repo, existingCommit, existingTagInfo, name );
        if( existingTagInfo.HasExpired )
        {
            var n = existingCommit.Version.SetBuildMetaData( null ).ToString();
            var vN = 'v' + n;
            monitor.Info( ScreenType.CKliScreenTag, $"Deprecation tag expired. Removing '{vN}' tag (from local and remote) in '{repo.DisplayPath}'." );

            var localTags = repo.GitRepository.Repository.Tags;
            if( localTags[n] != null )
            {
                localTags.Remove( n );
            }
            if( localTags[vN] != null )
            {
                localTags.Remove( vN );
            }
            repo.GitRepository.DeferredPushRefSpecs.Add( $":refs/tags/{n}" );
            repo.GitRepository.DeferredPushRefSpecs.Add( $":refs/tags/{vN}" );
        }
        else
        {
            monitor.Info( ScreenType.CKliScreenTag, $"Version tag '{name}' has been updated in '{repo.DisplayPath}'." );
        }
        return existingTagInfo;
    }

    static DeprecatedTagInfo CreateDeprecationTag( IActivityMonitor monitor, TagCommit existing, string? reason, int daysDelay )
    {
        Throw.DebugAssert( "We used GetWithoutIssue and existing is not a +fake.", existing.BuildContentInfo != null );
        var tagInfo = new DeprecatedTagInfo( existing.BuildContentInfo,
                                             DateOnly.FromDateTime( DateTime.UtcNow.AddDays( daysDelay ) ),
                                             daysDelay,
                                             reason ?? DeprecatedTagInfo.UnspecifiedReason );

        var name = $"v{existing.Version}+deprecated";
        AddTag( existing.Repo, existing, tagInfo, name );
        if( tagInfo.HasExpired )
        {
            monitor.Info( ScreenType.CKliScreenTag, $"Deprecation tag expired. Removing '{existing.Version.ParsedText}' tag (from local and remote) in '{existing.Repo.DisplayPath}'." );

            var localTags = existing.Repo.GitRepository.Repository.Tags;
            if( localTags[existing.Version.ParsedText] != null )
            {
                localTags.Remove( existing.Version.ParsedText );
            }
            existing.Repo.GitRepository.DeferredPushRefSpecs.Add( $":refs/tags/{existing.Version.ParsedText}" );
        }
        else
        {
            monitor.Info( ScreenType.CKliScreenTag, $"Version tag '{name}' has been created in '{existing.Repo.DisplayPath}'." );
        }
        return tagInfo;
    }

    void EnsureImpliedDeprecatedTag( IActivityMonitor monitor,
                                     RepoReleaseInfo origin,
                                     HashSet<RepoReleaseInfo> visited,
                                     List<RepoReleaseInfo> deprecated,
                                     List<RepoReleaseInfo> path,
                                     int daysDelay,
                                     DateOnly expiration )
    {
        Throw.DebugAssert( path[^1] == origin );
        foreach( var impact in origin.GetDirectConsumers( monitor ) )
        {
            if( visited.Add( impact ) )
            {
                var versionInfo = Get( monitor, impact.Repo );
                Throw.DebugAssert( "No version issue on any repo.", !versionInfo.HasIssue );
                if( !versionInfo.TryGetTagCommit( impact.Version, out var tagCommit ) )
                {
                    monitor.Warn( $"""
                        Version tag 'v{impact.Version}' in '{impact.Repo.DisplayPath}' not found.
                        {StoppingDeprecationMessage( monitor, impact )}
                        """ );
                }
                else if( tagCommit.IsFakeVersion )
                {
                    monitor.Warn( $"""
                        Tag 'v{impact.Version}' in '{impact.Repo.DisplayPath}' is a +fake one.
                        {StoppingDeprecationMessage( monitor, impact )}
                        """ );
                }
                else if( !tagCommit.IsDeprecatedVersion && versionInfo.AliveStable == tagCommit )
                {
                    // Same rule as the root of the deprecation (see DeprecateVersion): the version this
                    // repository currently offers to its own consumers cannot be taken away from it. The
                    // deprecation stops here rather than silently killing it - the repository must publish
                    // a replacement first, and the propagation can then be replayed.
                    monitor.Warn( $"""
                        Version 'v{impact.Version}' is the current version of '{impact.Repo.DisplayPath}': it cannot be deprecated.
                        {StoppingDeprecationMessage( monitor, impact )}
                        """ );
                }
                else
                {
                    // Tag exists, is not a fake one and is not the alive version: it may already be deprecated.
                    var tagInfo = tagCommit.DeprecatedInfo;
                    if( tagInfo != null )
                    {
                        // If the existing deprecation is planned but later than the
                        // current one: the earlier obviously wins.
                        if( tagInfo.Expiration > expiration )
                        {
                            tagInfo = UpdateExistingDeprecationTag( monitor,
                                                                    tagCommit,
                                                                    tagInfo,
                                                                    reason: null,
                                                                    tagInfo.DaysDelay,
                                                                    expiration );
                        }
                        // Even if this one is already deprecated, continue the propagation.
                    }
                    else
                    {
                        var sb = new StringBuilder( "Deprecated by " );
                        for( int i = path.Count - 1; i >= 0; --i )
                        {
                            var c = path[i];
                            sb.Append( c.Repo.DisplayPath ).Append( '/' ).Append( c.Version );
                            if( i > 0 ) sb.Append( " <- " );
                        }
                        tagInfo = CreateDeprecationTag( monitor, tagCommit, reason: sb.ToString(), daysDelay );
                    }
                    deprecated.Add( impact );
                    path.Add( impact );
                    EnsureImpliedDeprecatedTag( monitor, impact, visited, deprecated, path, daysDelay, expiration );
                    path.RemoveAt( path.Count - 1 );
                }
            }
        }

        static string StoppingDeprecationMessage( IActivityMonitor monitor, RepoReleaseInfo i )
        {
            return $"Stopping deprecation propagation on '{i}' and its direct consumers ('{i.GetDirectConsumers( monitor ).Select( i => i.ToString() ).Concatenate( "', '" )}').";
        }
    }

}
