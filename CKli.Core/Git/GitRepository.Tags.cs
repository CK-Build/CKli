using CK.Core;
using CommunityToolkit.HighPerformance;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

namespace CKli.Core;

public sealed partial class GitRepository
{
    /// <summary>
    /// Checks that a tag name is valid for CKli: it must obviously be non empty and contains
    /// only ascii characters and letters must be lowercase. 
    /// <para>
    /// Special characters like * or ? and / are allowed: this handles regular tag name and canonical tag
    /// names (start with "refs/tags/"): the standard Git tag prefix is compatible with this rule.
    /// </para>
    /// <para>
    /// See <see cref="GitTagInfo.InvalidTags"/>.
    /// </para>
    /// </summary>
    /// <param name="tagName">The tag name to test.</param>
    /// <returns>True if this is a valid tag name for CKli. False if this tag name must be ignored.</returns>
    public static bool IsCKliValidTagName( ReadOnlySpan<char> tagName )
    {
        if( tagName.IsEmpty ) return false;
        foreach( var c in tagName )
        {
            if( !char.IsAscii( c ) || char.IsAsciiLetter( c ) && !char.IsAsciiLetterLower( c ) )
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Deletes any number of local tags (empty <paramref name="tagNames"/> is a no-op).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tagNames">The tag names. They can be canonic (start with "refs/tags/") or regular (friendly name).</param>
    /// <returns>True on success, false on error.</returns>
    public bool DeleteLocalTags( IActivityMonitor monitor, IEnumerable<string> tagNames )
    {
        var names = tagNames.Concatenate();
        if( names.Length == 0 ) return true;
        try
        {
            monitor.Trace( $"Deleting local tags '{names}'." );
            foreach( var t in tagNames )
            {
                _git.Tags.Remove( t );
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "Error while deleting local tags.", ex );
            return false;
        }
    }

    /// <summary>
    /// Deletes any number of remote tags (empty <paramref name="tagNames"/> is a no-op).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tagNames">The tag names. They can be canonic (start with "refs/tags/") or regular.</param>
    /// <param name="remoteName">The remote name to consider.</param>
    /// <returns>True on success, false on error.</returns>
    public bool DeleteRemoteTags( IActivityMonitor monitor, List<string> tagNames, string remoteName = "origin" )
    {
        var names = tagNames.Concatenate();
        if( names.Length == 0 ) return true;
        try
        {
            if( !GetRemote( monitor, remoteName, forWrite: true, out var remote, out var creds ) )
            {
                return false;
            }
            monitor.Trace( $"Deleting remote tags '{names}' from '{remote.Name}'." );
            // This is the only push that doesn't go through Push (there is no DeferredPushRefSpecs to handle here):
            // it builds nothing but deletion ref specs that IsRefusedPushRefSpec never refuses (removing a "local/"
            // or "building/" reference from a remote is always allowed). Any other kind of spec added here must be
            // filtered by IsRefusedPushRefSpec.
            _git.Network.Push( remote, tagNames.Select( t => t.StartsWith( "refs/tags/", StringComparison.Ordinal )
                                                                ? $":{t}"
                                                                : $":refs/tags/{t}" ), new PushOptions()
            {
                CredentialsProvider = ( url, user, types ) => creds
            } );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "Error while deleting remote tags.", ex );
            return false;
        }
    }

    /// <summary>
    /// Gets the remote <see cref="GitTagInfo"/> from the specified remote.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tags">The remote <see cref="GitTagInfo"/> on success.</param>
    /// <param name="remoteName">The remote name.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public bool GetRemoteTags( IActivityMonitor monitor,
                               [NotNullWhen( true )] out GitTagInfo? tags,
                               string remoteName = "origin",
                               CancellationToken cancellation = default )
    {
        try
        {
            if( !GetRemote( monitor, remoteName, forWrite: false, out var remote, out var creds ) )
            {
                tags = null;
                return false;
            }
            var result = ImmutableArray.CreateBuilder<TagInfo>();
            ImmutableArray<string>.Builder? invalidTags = null;
            List<(string CanonicalName, string TargetIdentifier)>? missing = null;
            // Currently there's no way to cancel the ListReferences. We use cancellation in the loop.
            LibGit2Sharp.Handlers.CredentialsHandler credHandler = ( url, user, types ) => creds;
            var remoteRefs = _git.Network.ListReferences( remote, credHandler );
            foreach( var r in remoteRefs )
            {
                if( cancellation.IsCancellationRequested )
                {
                    tags = null;
                    return false;
                }
                var sName = r.CanonicalName.AsSpan();
                if( sName.StartsWith( "refs/tags/", StringComparison.Ordinal ) )
                {
                    if( sName.EndsWith( "^{}", StringComparison.Ordinal ) )
                    {
                        // We ignore the annotated tag reference.
                        continue;
                    }
                    if( !IsCKliValidTagName( sName.Slice( 10 ) ) )
                    {
                        invalidTags ??= ImmutableArray.CreateBuilder<string>();
                        invalidTags.Add( r.CanonicalName );
                        continue;
                    }
                    var dr = r.ResolveToDirectReference();
                    if( dr.Target is TagAnnotation a )
                    {
                        if( a.Target is Commit t )
                        {
                            result.Add( new TagInfo( r.CanonicalName, t, a ) );
                        }
                        else
                        {
                            monitor.Trace( $"Ignoring annotated tag '{r.CanonicalName}' that doesn't target a commit." );
                        }
                    }
                    else
                    {
                        var target = dr.Target;
                        if( target is Commit t )
                        {
                            result.Add( new TagInfo( r.CanonicalName, t, null ) );
                        }
                        else if( target != null )
                        {
                            monitor.Trace( $"Ignoring lightweight tag '{r.CanonicalName}' that doesn't target a commit." );
                        }
                        else
                        {
                            missing ??= new List<(string,string)>();
                            missing.Add( (r.CanonicalName, r.TargetIdentifier) );
                        }
                    }
                }
            }
            if( missing != null )
            {
                Commands.Fetch( _git,
                                "origin",
                                missing.Select( m => m.TargetIdentifier ),
                                new FetchOptions
                                {
                                    CredentialsProvider = credHandler,
                                    OnProgress = _ => !cancellation.IsCancellationRequested,
                                    OnTransferProgress = _ => !cancellation.IsCancellationRequested,
                                    OnUpdateTips = ( _, _, _ ) => !cancellation.IsCancellationRequested,
                                },
                                null );
                foreach( var m in missing )
                {
                    var target = _git.Lookup( new ObjectId( m.TargetIdentifier ) );
                    if( target == null )
                    {
                        monitor.Warn( ActivityMonitor.Tags.ToBeInvestigated, $"Unable to lookup fetched local tag '{m.CanonicalName}'." );
                    }
                    else
                    {
                        if( target is Commit t )
                        {
                            result.Add( new TagInfo( m.CanonicalName, t, null ) );
                        }
                        else if( target is TagAnnotation a && a.Target is Commit aC )
                        {
                            result.Add( new TagInfo( m.CanonicalName, aC, a ) );
                        }
                        else
                        {
                            monitor.Trace( $"Ignoring tag '{m.CanonicalName}' that doesn't target a commit." );
                        }
                    }
                }
            }
            result.Sort();
            tags = new GitTagInfo( result, invalidTags );
            return true;
        }
        catch( Exception ex )
        {
            // Avoid error dump if cancelled. We may miss "real exception" but we don't care.
            // This skips LibGit2Sharp's UserCancelledException.
            if( !cancellation.IsCancellationRequested )
            {
                monitor.Error( "Error while getting remote tags. This requires a manual fix.", ex );
            }
            tags = null;
            return false;
        }
    }

    /// <summary>
    /// Gets the local <see cref="GitTagInfo"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tags">The local <see cref="GitTagInfo"/> on success.</param>
    /// <returns>True on success, false on error.</returns>
    public bool GetLocalTags( IActivityMonitor monitor, [NotNullWhen( true )] out GitTagInfo? tags )
    {
        try
        {
            var result = ImmutableArray.CreateBuilder<TagInfo>();
            ImmutableArray<string>.Builder? invalidTags = null;
            foreach( var tag in _git.Tags )
            {
                if( !IsCKliValidTagName( tag.CanonicalName.AsSpan( 10 ) ) )
                {
                    invalidTags ??= ImmutableArray.CreateBuilder<string>();
                    invalidTags.Add( tag.CanonicalName );
                    continue;
                }
                if( tag.PeeledTarget is Commit t )
                {
                    result.Add( new TagInfo( tag.CanonicalName, t, tag.Annotation ) );
                }
                else
                {
                    monitor.Trace( $"Ignoring tag '{tag.CanonicalName}' that does't target a commit." );
                }
            }
            result.Sort();
            tags = new GitTagInfo( result, invalidTags );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "Error while listing tags. This requires a manual fix.", ex );
            tags = null;
            return false;
        }
    }

    /// <summary>
    /// Pulls any number of tags (empty <paramref name="tagNames"/> is a no-op).
    /// Local modifications of pulled tags are lost: use <see cref="FetchTags(IActivityMonitor, string, CancellationToken)"/> to safely 
    /// fetch remote-only tags.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tagNames">The tag names. They can be canonic (start with "refs/tags/") or regular.</param>
    /// <param name="remoteName">The remote name to consider.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public bool PullTags( IActivityMonitor monitor,
                          IEnumerable<string> tagNames,
                          string remoteName = "origin",
                          CancellationToken cancellation = default )
    {
        var names = tagNames.Concatenate();
        if( names.Length == 0 ) return true;
        try
        {
            if( !GetRemote( monitor, remoteName, forWrite: false, out var remote, out var creds ) )
            {
                return false;
            }
            var logMsg = $"Fetching tags '{names}' from '{remote.Name}'.";
            monitor.Trace( logMsg );
            Commands.Fetch( _git,
                            remote.Name,
                            tagNames.Select( t => t.StartsWith( "refs/tags/", StringComparison.Ordinal )
                                                                     ? $"+{t}:{t}"
                                                                     : $"+refs/tags/{t}:refs/tags/{t}" ),
                            new FetchOptions()
                            {
                                CredentialsProvider = ( url, user, types ) => creds,
                                TagFetchMode = TagFetchMode.None,
                                OnProgress = _ => !cancellation.IsCancellationRequested,
                                OnTransferProgress = _ => !cancellation.IsCancellationRequested,
                                OnUpdateTips = ( _, _, _ ) => !cancellation.IsCancellationRequested,
                            }, logMsg );
            return true;
        }
        catch( UserCancelledException )
        {
            // Avoid too many error dumps on cancellation.
            return false;
        }
        catch( Exception ex )
        {
            // Avoid error dump if cancelled. We may miss "real exception" but we don't care.
            // This skips LibGit2Sharp's UserCancelledException.
            if( !cancellation.IsCancellationRequested )
            {
                monitor.Error( "Error while pulling remote tags. This requires a manual fix.", ex );
            }
            return false;
        }
    }

    /// <summary>
    /// Pushes any number of tags (empty <paramref name="tagNames"/> is a no-op).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tagNames">The tag names. They can be canonic (start with "refs/tags/") or regular.</param>
    /// <param name="remoteName">The remote name to consider.</param>
    /// <returns>True on success, false on error.</returns>
    public bool PushTags( IActivityMonitor monitor, IEnumerable<string> tagNames, string remoteName = "origin" )
    {
        var names = tagNames.Concatenate();
        if( names.Length == 0 ) return true;
        monitor.Trace( $"Pushing tags '{names}' to '{remoteName}'." );

        if( !GetRemote( monitor, remoteName, forWrite: true, out var remote, out var creds ) )
        {
            return false;
        }
        return Push( monitor,
                     remote,
                     creds,
                     tagNames.Select( t => t.StartsWith( "refs/tags/", StringComparison.Ordinal )
                                                            ? $"+{t}"
                                                            : $"+refs/tags/{t}" ) );
    }

    /// <summary>
    /// Gets the diff between local and remote tags.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="diff">The diff between local and remote tags on success.</param>
    /// <param name="remoteName">The remote name.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public bool GetDiffTags( IActivityMonitor monitor,
                             [NotNullWhen( true )] out GitTagInfo.Diff? diff,
                             string remoteName = "origin",
                             CancellationToken cancellation = default )
    {
        if( !GetLocalTags( monitor, out var localTags )
            || !GetRemoteTags( monitor, out var remoteTags, remoteName, cancellation ) )
        {
            diff = null;
            return false;
        }
        diff = new GitTagInfo.Diff( localTags, remoteTags );
        return true;
    }


    /// <summary>
    /// Safely fetches remote only tags from <paramref name="remoteName"/> (pulls <see cref="GitTagInfo.Diff.RemoteOnlyTags"/>):
    /// this preserves any local tags.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="remoteName">The remote name to consider.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public bool FetchTags( IActivityMonitor monitor, string remoteName = "origin", CancellationToken cancellation = default )
    {
        if( !GetDiffTags( monitor, out var diff, remoteName, cancellation ) )
        {
            return false;
        }
        monitor.Trace( $"RemoteOnlyTags count: {diff.RemoteOnlyCount} (remote: '{remoteName}')." );
        if( diff.RemoteOnlyCount > 0 )
        {
            // This traces the pulled tags.
            return PullTags( monitor, diff.RemoteOnlyTags.Select( t => t.CanonicalName ), remoteName, cancellation );
        }
        return true;
    }
}
