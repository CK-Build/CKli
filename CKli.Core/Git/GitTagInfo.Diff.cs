using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

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
        readonly Stats _stats;

        internal struct Stats
        {
            internal int _conflictCount;
            internal int _localOnlyCount;
            internal int _remoteOnlyCount;
            internal int _commonCount;
            internal int _differCount;
        }

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

        /// <summary>
        /// Gets the number of tags with the same name that are on 2 different commits between
        /// local and remote: one of them must be deleted.
        /// <para>
        /// Conflict applies to <see cref="TagDiff.LocalOnly"/> and <see cref="TagDiff.RemoteOnly"/> <see cref="LocalRemoteTag"/>
        /// but a conflict by definition applies to a tag that "exists" on both side: the <see cref="LocalOnlyCount"/> and
        /// <see cref="RemoteOnlyCount"/> exclude tags that are in conflict.
        /// </para>
        /// </summary>
        public int ConflictCount => _stats._conflictCount;

        /// <summary>
        /// Gets the local/remote tags that don't target the same commit.
        /// </summary>
        public IEnumerable<LocalRemoteTag> Conflicts => _entries.SelectMany( e => e.Tags )
                                                                .Where( lr => (lr.Diff & (TagDiff.LocalOnly | TagDiff.CommitConflict)) == (TagDiff.LocalOnly | TagDiff.CommitConflict));

        /// <summary>
        /// Gets the number of tags that only exist locally.
        /// <para>
        /// This counts <see cref="TagDiff.LocalOnly"/>: <see cref="TagDiff.CommitConflict"/> are ignored. 
        /// </para>
        /// </summary>
        public int LocalOnlyCount => _stats._localOnlyCount;

        /// <summary>
        /// Gets the number of tags that only exist remotely.
        /// <para>
        /// This counts <see cref="TagDiff.RemoteOnly"/>: <see cref="TagDiff.CommitConflict"/> are ignored. 
        /// </para>
        /// </summary>
        public int RemoteOnlyCount => _stats._remoteOnlyCount;

        /// <summary>
        /// Gets the number of tags that exist both locally and remotely without conflict (they target the same commit).
        /// Among them, <see cref="DifferCount"/> may be different.
        /// </summary>
        public int CommonCount => _stats._commonCount;

        /// <summary>
        /// Gets the number of tags that exist both locally and remotely with a different kind (annotated vs. lightweight),
        /// a different annotated message or a different <see cref="TagAnnotation.Tagger"/>.
        /// </summary>
        public int DifferCount => _stats._differCount;

        internal Diff( GitTagInfo local, GitTagInfo remote )
        {
            _local = local;
            _remote = remote;
            _stats = new Stats();
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
                    b.Add( new DiffEntry( localIndex, eL.Current, remoteIndex, null, ref _stats ) );
                }
                else
                {
                    int cmp = eL.Current.CompareTo( eR.Current );
                    while( cmp > 0 )
                    {
                        b.Add( new DiffEntry( localIndex, null, remoteIndex, eR.Current, ref _stats ) );
                        if( (hasR && (hasR = eR.MoveNext())) is false )
                        {
                            b.Add( new DiffEntry( localIndex, eL.Current, remoteIndex, null, ref _stats ) );
                            break;
                        }
                        cmp = eL.Current.CompareTo( eR.Current );
                    }
                    if( hasR )
                    {
                        if( cmp == 0 )
                        {
                            b.Add( new DiffEntry( localIndex, eL.Current, remoteIndex, eR.Current, ref _stats ) );
                            hasR = eR.MoveNext();
                        }
                        else
                        {
                            Throw.DebugAssert( cmp < 0 );
                            b.Add( new DiffEntry( localIndex, eL.Current, remoteIndex, null, ref _stats ) );
                        }
                    }
                }
            }
            while( hasR )
            {
                b.Add( new DiffEntry( localIndex, null, remoteIndex, eR.Current, ref _stats ) );
                hasR = eR.MoveNext();
            }
            _entries = b.DrainToImmutable();
        }

        /// <summary>
        /// Renders this difference.
        /// </summary>
        /// <param name="s">The screen type.</param>
        /// <param name="orderByTagName">
        /// True to order tags by their name rather than their target commit date that is the default.
        /// This is mainly for tests.
        /// </param>
        /// <param name="withFetchRequired">Displays the <see cref="UnavailableRemoteTags"/>.</param>
        /// <param name="withLocalInvalidTags">Displays the local tags that are invalid (see <see cref="GitTagInfo.InvalidTags"/>).</param>
        /// <param name="withRemoteInvalidTags">Displays the remote tags that are invalid (see <see cref="GitTagInfo.InvalidTags"/>).</param>
        /// <param name="withConflicts">Displays the tags that are in conflict (see <see cref="Conflicts"/>).</param>
        /// <param name="withRegularTags">Displays the regular tags (exists on both sides and have no issues).</param>
        /// <param name="withLocalOnlyTags">Displays the tags that are only local.</param>
        /// <param name="withRemoteOnlyTags">Displays the tags that are only on the remote.</param>
        /// <param name="withDifferences">Displays the tags that exists on both sides and differ with the detail of their differences.</param>
        /// <returns>The renderable.</returns>
        public IRenderable ToRenderable( ScreenType s,
                                         bool orderByTagName = false,
                                         bool withFetchRequired = true,
                                         bool withLocalInvalidTags = true,
                                         bool withRemoteInvalidTags = true,
                                         bool withConflicts = true,
                                         bool withRegularTags = true,
                                         bool withLocalOnlyTags = true,
                                         bool withRemoteOnlyTags = true,
                                         bool withDifferences = true )
        {
            var display = s.Unit;
            if( withFetchRequired && FetchRequired )
            {
                display = display.AddBelow(
                    s.Text( $"Unavailable remote tags. A 'ckli fetch' MAY enable target commits resolution for:", foreColor: ConsoleColor.Yellow ),
                    s.Text( $"- {UnavailableRemoteTags.Concatenate()}.", foreColor: ConsoleColor.DarkYellow ) );
            }
            if( withLocalInvalidTags && _local.InvalidTags.Length > 0 )
            {
                display = display.AddBelow( _local.InvalidTagsToRenderable( s, "local ignored tags" ) );
            }
            if( withRemoteInvalidTags && _remote.InvalidTags.Length > 0 )
            {
                display = display.AddBelow( _remote.InvalidTagsToRenderable( s, "remote ignored tags" ) );
            }
            if( withConflicts && _stats._conflictCount > 0 )
            {
                var conflicts = Conflicts;
                if( orderByTagName ) conflicts = conflicts.OrderBy( c => c.CanonicalName );
                display = display.AddBelow(
                    s.Text( $"⚠ {_stats._conflictCount} conflicts:", foreColor: ConsoleColor.Red ),
                    Conflicts.Select( c => s.Text( $"- Tag '{c.ShortName}' is locally on '{c.Commit.Id.Sha.AsSpan( 0, 7)}' but targets '{c.ConflictCommitId}' on the remote.",
                                                   foreColor: ConsoleColor.DarkRed ) ) );
            }
            if( display == s.Unit && _entries.Length == 0 )
            {
                display = display.AddBelow( s.Text( "No local nor remote tags." ) );
            }
            else
            {
                // Counting local/remote/diff here is for debug asserting the
                // computed Stats values.
                StringBuilder regular = new StringBuilder();
                int localCount = 0;
                StringBuilder localOnly = new StringBuilder();
                int remoteCount = 0;
                StringBuilder remoteOnly = new StringBuilder();
                int diffCount = 0;
                StringBuilder difference = new StringBuilder();
                var allLR = _entries.SelectMany( e => e.Tags );
                if( orderByTagName ) allLR = allLR.OrderBy( c => c.CanonicalName );
                foreach( var lr in allLR )
                {
                    if( lr.Diff == TagDiff.None )
                    {
                        if( regular.Length > 0 ) regular.Append( ", " );
                        regular.Append( lr.ShortName );
                        continue;
                    }
                    if( (lr.Diff & TagDiff.CommitConflict) != 0 ) continue;
                    if( lr.Diff == TagDiff.LocalOnly )
                    {
                        ++localCount;
                        if( localOnly.Length > 0 ) localOnly.Append( ", " );
                        localOnly.Append( lr.ShortName );
                    }
                    else if( lr.Diff == TagDiff.RemoteOnly )
                    {
                        ++remoteCount;
                        if( remoteOnly.Length > 0 ) remoteOnly.Append( ", " );
                        remoteOnly.Append( lr.ShortName );
                    }
                    else
                    {
                        Throw.DebugAssert( (lr.Diff & TagDiff.DifferMask) != 0 );
                        ++diffCount;
                        if( difference.Length > 0 ) difference.AppendLine();

                        difference.Append( "- '" ).Append( lr.ShortName ).Append( "' " );
                        if( lr.Diff == TagDiff.LocalAnnotatedDiffer )
                        {
                            difference.Append( "is locally an annotated tag but remotely a lightweight one." );
                        }
                        else if( lr.Diff == TagDiff.LocalAnnotatedDiffer )
                        {
                            difference.Append( "is locally a lightweight tag but remotely an annotated one." );
                        }
                        else
                        {
                            Throw.DebugAssert( (lr.Diff & (TagDiff.AnnotationMessageDiffer | TagDiff.AnnotationTaggerDiffer)) != 0 );
                            Throw.DebugAssert( lr.Local?.Annotation?.Message != null );
                            Throw.DebugAssert( lr.Remote?.Annotation?.Message != null );

                            difference.Append( "has:" ).AppendLine();
                            if( (lr.Diff & TagDiff.AnnotationMessageDiffer) != 0 )
                            {
                                difference.Append( "│ <local message>" ).AppendLine()
                                          .AppendMultiLine( "│  ", lr.Local.Annotation.Message, true )
                                          .AppendLine()
                                          .Append( "│ <remote message>" ).AppendLine()
                                          .AppendMultiLine( "│  ", lr.Remote.Annotation.Message, true )
                                          .AppendLine();
                            }
                            if( (lr.Diff & TagDiff.AnnotationTaggerDiffer) != 0 )
                            {
                                difference.Append( "│ <Local tagger>" ).AppendLine()
                                          .Append( "│  " ).Append( lr.Local.Annotation.Tagger.Name )
                                                          .Append( " (" ).Append( lr.Local.Annotation.Tagger.Email ).Append( ')' )
                                                          .AppendLine()
                                          .Append( "│  on " ).Append( lr.Local.Annotation.Tagger.When.UtcDateTime.ToString( "u" ) ).AppendLine()
                                          .Append( "│ <remote signature>" ).AppendLine()
                                          .Append( "│  " ).Append( lr.Remote.Annotation.Tagger.Name )
                                                          .Append( " (" ).Append( lr.Remote.Annotation.Tagger.Email ).Append( ')' )
                                                          .AppendLine()
                                          .Append( "│  on " ).Append( lr.Remote.Annotation.Tagger.When.UtcDateTime.ToString( "u" ) ).AppendLine()
                                          .AppendLine();
                            }
                        }
                    }
                }
                Throw.DebugAssert( localCount == _stats._localOnlyCount );
                Throw.DebugAssert( remoteCount == _stats._remoteOnlyCount );
                Throw.DebugAssert( diffCount == _stats._differCount );
                if( withRegularTags && regular.Length > 0 )
                {
                    display = display.AddBelow( s.Text( regular.ToString() ) );
                }
                if( withLocalOnlyTags && localCount > 0 )
                {
                    display = display.AddBelow( s.Text( $"{localCount} local only:", foreColor: ConsoleColor.Blue ),
                                                s.Text( localOnly.ToString() ) );

                }
                if( withRemoteOnlyTags && remoteCount > 0 )
                {
                    display = display.AddBelow( s.Text( $"{remoteCount} remote only:", foreColor: ConsoleColor.Blue ),
                                                s.Text( remoteOnly.ToString() ) );

                }
                if( withDifferences && diffCount > 0 )
                {
                    display = display.AddBelow( s.Text( $"{diffCount} differences:", foreColor: ConsoleColor.Magenta ),
                                                s.Text( difference.ToString() ) );

                }
            }
            return display;
        }

    }
}
