using CK.Core;
using CKli.Core;
using System.Collections.Generic;

namespace CKli.Publish.Plugin;

/// <summary>
/// Models a list of world releases to be published.
/// This capacity to handle multiple <see cref="WorldReleaseInfo"/> is currently not used (only one publication at a time
/// is done).
/// <para>
/// This acts as a FIFO (a queue): by forwarding the <see cref="PrimaryCursor"/>, the <see cref="Releases"/> are removed.
/// </para>
/// </summary>
sealed partial class PublishState
{
    readonly List<WorldReleaseInfo> _releases;
    readonly World _world;
    Cursor _primaryCursor;

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets the list of world releases that wait to be published.
    /// </summary>
    public IReadOnlyList<WorldReleaseInfo> Releases => _releases;

    /// <summary>
    /// Gets the cursor associated to this state.
    /// </summary>
    public Cursor PrimaryCursor => _primaryCursor;

    /// <summary>
    /// Updates the <see cref="PrimaryCursor"/> by forwarding it and removes from <see cref="Releases"/>
    /// the world releases that are before the new cursor.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="offset">The number of positions. Must be positive.</param>
    /// <param name="cleanupReleases">False to keep world releases even if the resulting cursor is after them.</param>
    /// <returns>The updated <see cref="PrimaryCursor"/>.</returns>
    public Cursor ForwardPrimaryCursor( IActivityMonitor monitor, int offset, bool cleanupReleases = true )
    {
        Throw.CheckArgument( offset > 0 );
        var c = _primaryCursor.Forward( offset );
        // Quick: consider only a change of the World.
        if( cleanupReleases && c.World != _primaryCursor.World )
        {
            Throw.DebugAssert( c.World is null == c.Location is Cursor.LocType.EndOfState );
            if( c.World != null )
            {
                while( _releases.Count > 0 )
                {
                    if( _releases[0] != c.World )
                    {
                        _releases.RemoveAt( 0 );
                    }
                }
            }
            else
            {
                // World is null <=> EndOfState.
                _releases.Clear();
            }
        }
        return _primaryCursor = c;
    }

    /// <summary>
    /// Adds a new world release and persists the new state.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="newOne">The new world release.</param>
    public void Add( IActivityMonitor monitor, WorldReleaseInfo newOne )
    {
        Throw.CheckArgument( _releases.Count == 0 || newOne.BuildDate > _releases[^1].BuildDate );
        _releases.Add( newOne );
        if( _primaryCursor.Location == Cursor.LocType.EndOfState )
        {
            _primaryCursor = CreateCursor();
        }
    }

    /// <summary>
    /// Creates a new <see cref="Cursor"/> positioned at the start of this state.
    /// </summary>
    /// <returns>The cursor to use to traverse this state.</returns>
    public Cursor CreateCursor( int position = 0 ) => Cursor.Create( this );

    internal PublishState( World world )
    {
        _world = world;
        _releases = new List<WorldReleaseInfo>();
        _primaryCursor = new Cursor( this );
    }

    PublishState( World world, NormalizedPath path, List<WorldReleaseInfo> releases, int primaryPosition )
    {
        _world = world;
        _releases = releases;
        _primaryCursor = new Cursor( this ).Forward( primaryPosition );
    }
}

