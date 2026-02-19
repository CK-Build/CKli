using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;

namespace CKli.Core;

public sealed partial class GitTagInfo
{
    /// <summary>
    /// Captures a local/remote tag with the same canonical name on the same
    /// commit and their potential difference.
    /// </summary>
    public sealed class LocalRemoteTag
    {
        readonly TagInfo? _local;
        readonly TagInfo? _remote;
        readonly TagInfo? _conflict;
        readonly TagDiff _diff;

        internal LocalRemoteTag( TagInfo here, TagDiff d, IReadOnlyDictionary<string, TagInfo> oppositeIndex, ref Diff.Stats stats )
        {
            Throw.DebugAssert( d is TagDiff.LocalOnly or TagDiff.RemoteOnly );

            _diff = DetectCommitConflict( here, d, oppositeIndex, out _conflict );

            if( (d & TagDiff.LocalOnly) != 0 )
            {
                // Don't consider conflict as a "local only" tag.
                if( (_diff & TagDiff.CommitConflict) != 0 )
                {
                    ++stats._conflictCount;
                }
                else
                {
                    ++stats._localOnlyCount;
                }
                _local = here;
                _remote = null;
            }
            else
            {
                // Don't consider conflict as a "remote only" tag.
                if( (_diff & TagDiff.CommitConflict) == 0 )
                {
                    ++stats._remoteOnlyCount;
                }
                _local = null;
                _remote = here;
            }

            static TagDiff DetectCommitConflict( TagInfo here,
                                                 TagDiff d,
                                                 IReadOnlyDictionary<string, TagInfo> oppositeIndex,
                                                 out TagInfo? conflict )
            {
                conflict = null;
                Throw.DebugAssert( "On this side, this is necessarily NOT a 'fetch required' tag.", here.Commit != null );
                Throw.DebugAssert( "And if the Tag name exists on the other side, it is not this one.",
                                   !oppositeIndex.TryGetValue( here.CanonicalName, out var there ) || there != here );
                if( oppositeIndex.TryGetValue( here.CanonicalName, out var onOtherSide ) && here.Commit.Sha != onOtherSide.Commit?.Sha )
                {
                    conflict = onOtherSide;
                    d |= TagDiff.CommitConflict;
                }
                return d;
            }
        }

        internal LocalRemoteTag( TagInfo local, TagInfo remote, TagDiff diff )
        {
            Throw.DebugAssert( local != null || remote != null );
            _local = local;
            _remote = remote;
            _diff = diff;
        }


        TagInfo Info => _local ?? _remote!;

        /// <summary>
        /// Gets the local tag information if it exists locally.
        /// </summary>
        public TagInfo? Local => _local;

        /// <summary>
        /// Gets the remote tag information if it exists remotely.
        /// </summary>
        public TagInfo? Remote => _remote;

        /// <summary>
        /// Gets the tag name prefixed by "refs/tags/".
        /// </summary>
        public string CanonicalName => Info.CanonicalName;

        /// <summary>
        /// Gets the tag name without "refs/tags/" prefix.
        /// </summary>
        public ReadOnlySpan<char> ShortName => Info.ShortName;

        /// <summary>
        /// Gets the commit.
        /// </summary>
        public Commit Commit => Info.Commit!;

        /// <summary>
        /// Gets the tag difference.
        /// </summary>
        public TagDiff Diff => _diff;

        /// <summary>
        /// Gets the conflicting tag with the same name but on a different commit.
        /// <para>
        /// Non null only if <see cref="TagDiff.CommitConflict"/> bit is set.
        /// </para>
        /// </summary>
        public TagInfo? Conflict => _conflict;

        /// <summary>
        /// Gets either "unavailable" if the <see cref="Conflict"/> is not available (fetch-required)
        /// or the shorten Sha otherwise.
        /// <para>
        /// Always null if there's not conflict.
        /// </para>
        /// </summary>
        public string? ConflictCommitId
        {
            get
            {
                if( _conflict == null ) return null;
                var c = _conflict.Commit;
                return c == null ? "unavailable" : c.Id.Sha.Substring( 0, 7 );
            }
        }

        /// <summary>
        /// Returns the canonical name and the <see cref="TagDiff"/>.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{CanonicalName} - Diff: {_diff}.";
    }
}
