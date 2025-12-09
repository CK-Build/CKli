using CK.Core;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static CKli.Core.GitTagInfo;

namespace CKli.Core;

public sealed partial class GitTagInfo
{
    /// <summary>
    /// Captures the differences between local and remote tags.
    /// </summary>
    public sealed class Diff
    {
        readonly GitTagInfo _local;
        readonly GitTagInfo _remote;
        readonly ImmutableArray<DiffEntry> _entries;
        ImmutableArray<string> _unavailableRemoteTags;

        /// <summary>
        /// Gets whether fetching branches is required to obtain information
        /// on at least one remote tag. See <see cref="UnavailableRemoteTags"/>.
        /// </summary>
        public bool FetchRequired => _remote._fetchRequiredCount > 0;

        /// <summary>
        /// Gets the canonical tag names (starting with "refs/tags/") that exist on the remote
        /// but for which target objects are not locally available. Fetching the branches that
        /// contain commits referenced by these tags will make the target objects available
        /// (without fetching the tags themselves).
        /// </summary>
        public ImmutableArray<string> UnavailableRemoteTags
        {
            get
            {
                return _unavailableRemoteTags.IsDefault
                         ? (_unavailableRemoteTags = [.. _remote._tags.Take( _remote._fetchRequiredCount ).Select( i => i.CanonicalName )])
                         : _unavailableRemoteTags;
            }
        }

        /// <summary>
        /// Gets the <see cref="DiffEntry"/> between local and remote.
        /// </summary>
        public ImmutableArray<DiffEntry> Entries => _entries;

        internal Diff( GitTagInfo local, GitTagInfo remote )
        {
            _local = local;
            _remote = remote;
            // Ensure groups initialization.
            var gLocal = local.GroupedTags;
            Throw.DebugAssert( local._fetchRequiredCount == 0 );
            var gRemote = remote.GroupedTags;
            // For TagDiff.CommitConflict we need the indexed tags on both sides.
            // They are provided to the Entry ctor and are called for each TagInfo
            // on their opposite side.
            var localIndex = local.IndexedTags;
            var remoteIndex = remote.IndexedTags;

            // Skips the fetch-required tags.
            // We use a enumerator forward diff below because we can: the TagInfo are strictly ordered.
            var eR = gRemote.GetEnumerator();
            for( int i = 0; i < remote._fetchRequiredCount; ++i )
            {
                bool skipped = eR.MoveNext();
                Throw.DebugAssert( skipped );
            }
            var eL = gLocal.GetEnumerator();
            int estimatedCount = Math.Max( gLocal.Length, gRemote.Length );
            var b = ImmutableArray.CreateBuilder<DiffEntry>( estimatedCount + estimatedCount / 10 );
            bool hasR = eR.MoveNext();
            while( eL.MoveNext() )
            {
                if( !hasR )
                {
                    b.Add( new DiffEntry( localIndex, eL.Current, remoteIndex, null ) );
                }
                else
                {
                    int cmp = eL.Current.CompareTo( eR.Current );
                    while( cmp > 0 )
                    {
                        b.Add( new DiffEntry( localIndex, null, remoteIndex, eR.Current ) );
                        if( (hasR && (hasR = eR.MoveNext())) is false ) break;
                        cmp = eL.Current.CompareTo( eR.Current );
                    }
                    if( hasR )
                    {
                        if( cmp == 0 )
                        {
                            b.Add( new DiffEntry( localIndex, eL.Current, remoteIndex, eR.Current ) );
                            hasR = eR.MoveNext();
                        }
                        else
                        {
                            Throw.DebugAssert( cmp < 0 );
                            b.Add( new DiffEntry( localIndex, eL.Current, remoteIndex, null ) );
                        }
                    }
                }
            }
            while( hasR )
            {
                b.Add( new DiffEntry( localIndex, null, remoteIndex, eR.Current ) );
                hasR = eR.MoveNext();
            }
            _entries = b.DrainToImmutable();
        }
    }
}
