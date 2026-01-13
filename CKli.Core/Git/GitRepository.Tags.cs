using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CKli.Core;

public sealed partial class GitRepository
{
    /// <summary>
    /// Checks that a tag name is valid for CKli: it must obviously be non empty and contains
    /// only ascii characters and letters must be lowercase.
    /// <para>
    /// This handles regular tag name and canonical tag names (start with "refs/tags/"): the standard
    /// Git tag prefix is compatible with this rule.
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
    /// <param name="tagNames">The tag names. They can be canonic (start with "refs/tags/") or regular.</param>
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
    /// <returns>True on success, false on error.</returns>
    public bool GetRemoteTags( IActivityMonitor monitor, [NotNullWhen( true )] out GitTagInfo? tags, string remoteName = "origin" )
    {
        try
        {
            if( !GetRemote( monitor, remoteName, forWrite: false, out var remote, out var creds ) )
            {
                tags = null;
                return false;
            }
            var result = ImmutableArray.CreateBuilder<TagInfo>();
            ImmutableArray<TagInfo>.Builder? invalidTags = null;
            int fetchRequiredCount = 0;
            var remoteRefs = _git.Network.ListReferences( remote, ( url, user, types ) => creds );
            foreach( var r in remoteRefs )
            {
                var sName = r.CanonicalName.AsSpan();
                if( sName.StartsWith( "refs/tags/", StringComparison.Ordinal ) )
                {
                    if( sName.EndsWith( "^{}", StringComparison.Ordinal ) )
                    {
                        // We ignore the annotated tag reference.
                        continue;
                    }
                    var dr = r.ResolveToDirectReference();
                    if( dr.Target is TagAnnotation a )
                    {
                        if( a.Target is Commit t )
                        {
                            CollectTag( result, r.CanonicalName, t, a, ref fetchRequiredCount, ref invalidTags );
                        }
                        else
                        {
                            monitor.Trace( $"Ignoring annotated tag '{r.CanonicalName}' that does't target a commit." );
                        }
                    }
                    else
                    {
                        var target = dr.Target;
                        if( target is Commit t )
                        {
                            CollectTag( result, r.CanonicalName, t, null, ref fetchRequiredCount, ref invalidTags );
                        }
                        else if( target != null )
                        {
                            monitor.Trace( $"Ignoring lightweight tag '{r.CanonicalName}' that does't target a commit." );
                        }
                        else
                        {
                            // The target is not locally available. We cannot know if it's a
                            // commit.
                            CollectTag( result, r.CanonicalName, null, null, ref fetchRequiredCount, ref invalidTags );
                        }
                    }
                }
            }
            result.Sort();
            tags = new GitTagInfo( result, invalidTags, fetchRequiredCount );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "Error while getting remote tags. This requires a manual fix.", ex );
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
            ImmutableArray<TagInfo>.Builder? invalidTags = null;
            int fetchRequiredCount = 0;
            foreach( var tag in _git.Tags )
            {
                if( tag.PeeledTarget is Commit t )
                {
                    CollectTag( result, tag.CanonicalName, t, tag.Annotation, ref fetchRequiredCount, ref invalidTags );
                }
                else
                {
                    monitor.Trace( $"Ignoring tag '{tag.CanonicalName}' that does't target a commit." );
                }
            }
            Throw.DebugAssert( fetchRequiredCount == 0 );
            result.Sort();
            tags = new GitTagInfo( result, invalidTags, 0 );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "Error while listing tags. This requires a manual fix.", ex );
            tags = null;
            return false;
        }
    }

    static void CollectTag( ImmutableArray<TagInfo>.Builder result,
                            string canonicalName,
                            Commit? commit,
                            TagAnnotation? annotation,
                            ref int fetchRequiredCount,
                            ref ImmutableArray<TagInfo>.Builder? invalidTags )
    {
        var newOne = new TagInfo( canonicalName, commit, annotation );
        if( !IsCKliValidTagName( canonicalName.AsSpan( 10 ) ) )
        {
            invalidTags ??= ImmutableArray.CreateBuilder<TagInfo>();
            invalidTags.Add( newOne );
        }
        else
        {
            if( commit == null ) ++fetchRequiredCount;
            result.Add( newOne );
        }
    }

    /// <summary>
    /// Pulls any number of tags (empty <paramref name="tagNames"/> is a no-op).
    /// Local modifications of pulled tags are lost.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tagNames">The tag names. They can be canonic (start with "refs/tags/") or regular.</param>
    /// <param name="remoteName">The remote name to consider.</param>
    /// <returns>True on success, false on error.</returns>
    public bool PullTags( IActivityMonitor monitor, IEnumerable<string> tagNames, string remoteName = "origin" )
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
                                TagFetchMode = TagFetchMode.None
                            }, logMsg );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "Error while pulling remote tags. This requires a manual fix.", ex );
            return false;
        }
    }

    /// <summary>
    /// Pushes any number of tags (empty <paramref name="tagNames"/> is a no-op).
    /// Modifications of the remote tags are lost: the local replace them.
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
    /// <returns>True on success, false on error.</returns>
    public bool GetDiffTags( IActivityMonitor monitor, [NotNullWhen( true )] out GitTagInfo.Diff? diff, string remoteName = "origin" )
    {
        if( !GetLocalTags( monitor, out var localTags )
            || !GetRemoteTags( monitor, out var remoteTags, remoteName ) )
        {
            diff = null;
            return false;
        }
        diff = new GitTagInfo.Diff( localTags, remoteTags );
        return true;
    }
}
