using CK.Core;
using CKli.ArtifactHandler.Plugin;

namespace CKli.Publish.Plugin;

sealed partial class DirectPublisher
{
    /// <summary>
    /// Immutable logical cursor that identifies a position in a <see cref="DirectPublisher"/> and can
    /// be forwarded. This drives the publishing process.
    /// </summary>
    public sealed class Cursor
    {
        readonly DirectPublisher _state;
        readonly RepoInfo? _repo;
        readonly int _itemIndex;
        readonly LocType _location;

        /// <summary>
        /// Describes a current position in a <see cref="DirectPublisher"/>.
        /// </summary>
        public enum LocType
        {
            /// <summary>
            /// The <see cref="Repo"/> must be published.
            /// </summary>
            BegOfRepo,

            /// <summary>
            /// The <see cref="Repo"/>'s package <see cref="ItemIndex"/> in <see cref="BuildContentInfo.Produced"/> must be published.
            /// </summary>
            InPackage,

            /// <summary>
            /// The <see cref="Repo"/>'s file <see cref="ItemIndex"/> in <see cref="BuildContentInfo.AssetFileNames"/> must be published.
            /// </summary>
            InFile,

            /// <summary>
            /// All the <see cref="Repo"/>'s packages and files have been published.
            /// </summary>
            EndOfRepo,

            /// <summary>
            /// All the <see cref="World"/>'s Repos have been published (<see cref="Repo"/> is null).
            /// </summary>
            EndOfWorld,

            /// <summary>
            /// End of state has been reached. There's nothing more to do, both <see cref="World"/> and <see cref="Repo"/> are null.
            /// </summary>
            EndOfState

        }

        /// <summary>
        /// Gets the state.
        /// </summary>
        public DirectPublisher State => _state;

        /// <summary>
        /// Gets the current location.
        /// </summary>
        public LocType Location => _location;

        /// <summary>
        /// Gets whether this cursor is at the end of the <see cref="State"/>.
        /// </summary>
        public bool IsEndOfState => _location == LocType.EndOfState;

        /// <summary>
        /// Gets the current repository.
        /// Null when <see cref="Location"/> is <see cref="LocType.EndOfWorld"/> or <see cref="LocType.EndOfState"/>.
        /// </summary>
        public RepoInfo? Repo => _repo;

        /// <summary>
        /// Gets the index in the <see cref="RepoInfo.BuildContentInfo"/>'s <see cref="BuildContentInfo.Produced"/> packages or <see cref="BuildContentInfo.AssetFileNames"/>.
        /// When <see cref="Location"/> is not <see cref="LocType.InFile"/> or <see cref="LocType.InPackage"/>, this is -1.
        /// </summary>
        public int ItemIndex => _itemIndex;

        /// <summary>
        /// Returns a cursor that is on the next position in the <see cref="State"/>.
        /// <para>
        /// If the current <see cref="World"/> doesn't appear anymore in the <see cref="DirectPublisher.Releases"/>
        /// the returned cursor is on <see cref="LocType.EndOfState"/>.
        /// </para>
        /// </summary>
        /// <returns>The cursor on the next position.</returns>
        public Cursor Forward()
        {
            if( _location == LocType.EndOfState )
            {
                return this;
            }
            if( _location == LocType.EndOfWorld )
            {
                return new Cursor( _state );
            }
            Throw.DebugAssert( _repo != null );
            if( _location == LocType.BegOfRepo )
            {
                return EnterRepoBody( _state, _repo );
            }
            int nextIdx;
            if( _location == LocType.EndOfRepo )
            {
                nextIdx = _repo.Index + 1;
                return nextIdx == _state._repos.Length
                    ? new Cursor( _state, LocType.EndOfWorld, null, -1 )
                    : EnterRepo( _state, _state._repos[nextIdx] );
            }
            if( _location == LocType.InPackage )
            {
                nextIdx = _itemIndex + 1;
                if( nextIdx < _repo.BuildContentInfo.Produced.Length )
                {
                    return new Cursor( _state, LocType.InPackage, _repo, nextIdx );
                }
                if( _repo.BuildContentInfo.AssetFileNames.Length > 0 )
                {
                    return new Cursor( _state, LocType.InFile, _repo, 0 );
                }
                return new Cursor( _state, LocType.EndOfRepo, _repo, -1 );
            }
            Throw.DebugAssert( _location == LocType.InFile );
            nextIdx = _itemIndex + 1;
            return nextIdx < _repo.BuildContentInfo.AssetFileNames.Length
                ? new Cursor( _state, LocType.InFile, _repo, nextIdx )
                : new Cursor( _state, LocType.EndOfRepo, _repo, -1 );
        }

        /// <summary>
        /// Returns a cursor with a forwarded position in the <see cref="State"/>.
        /// </summary>
        /// <param name="offset">The offset to the current position. Must not be negative.</param>
        /// <returns>The cursor on the next position.</returns>
        public Cursor Forward( int offset )
        {
            Throw.CheckArgument( offset >= 0 );
            var c = this;
            while( --offset >= 0 && c.Location != LocType.EndOfState )
            {
                c = c.Forward();
            }
            return c;
        }

        /// <summary>
        /// Gets the position of this cursor in the <see cref="State"/>.
        /// <para>
        /// This returns -1 when <see cref="Location"/> is <see cref="LocType.EndOfState"/>.
        /// </para>
        /// </summary>
        /// <returns>This cursor position in the <see cref="State"/>. -1 when this cursor is no more in the state.</returns>
        public int GetPosition()
        {
            if( _location == LocType.EndOfState )
            {
                return -1;
            }
            if( _location == LocType.EndOfWorld )
            {
                return _state._publishedLength;
            }
            Throw.DebugAssert( _repo != null );
            int len = 0;
            foreach( var r in _state._repos )
            {
                if( r == _repo ) break;
                len += r.PublishedLength;
            }
            if( _location == LocType.EndOfRepo )
            {
                return len + _repo.PublishedLength;
            }
            ++len;
            if( _location == LocType.BegOfRepo )
            {
                return len;
            }
            if( _location == LocType.InPackage )
            {
                return len + _itemIndex;
            }
            Throw.DebugAssert( _location == LocType.InFile );
            return len + _repo.BuildContentInfo.Produced.Length + _itemIndex;
        }

        /// <summary>
        /// Empty (EndOfState) constructor. 
        /// </summary>
        /// <param name="state">The state.</param>
        internal Cursor( DirectPublisher state )
            : this( state, LocType.EndOfState, null, -1 )
        {
        }

        Cursor( DirectPublisher state, LocType location, RepoInfo? repo, int index )
        {
            _state = state;
            _repo = repo;
            _itemIndex = index;
            _location = location;
        }

        internal static Cursor Create( DirectPublisher state )
        {
            return state.Repos.Length == 0
                       ? new Cursor( state, LocType.EndOfWorld, null, -1 )
                       : EnterRepo( state, state.Repos[0] );
        }

        static Cursor EnterRepo( DirectPublisher state, RepoInfo repo )
        {
            // We come from Create() or _location == LocType.EndOfRepo => BegOfRepo, even
            // if the Repo is empty.
            return new Cursor( state, LocType.BegOfRepo, repo, -1 );
        }

        static Cursor EnterRepoBody( DirectPublisher state, RepoInfo repo )
        {
            // We come from LocType.BegOfRepo:
            // - Transitions to InPackage (if there's at least one package).
            // - InFile otherwise (if there's at least one file)
            // - or transitions directly to EndOfRepo.
            var loc = LocType.EndOfRepo;
            int index = -1;
            if( repo.BuildContentInfo.Produced.Length > 0 )
            {
                loc = LocType.InPackage;
                index = 0;
            }
            else if( repo.BuildContentInfo.AssetFileNames.Length > 0 )
            {
                loc = LocType.InFile;
                index = 0;
            }
            return new Cursor( state, loc, repo, index );
        }

    }
}

