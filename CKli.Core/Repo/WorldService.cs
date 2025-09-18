using CK.Core;
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

/// <summary>
/// Non generic base for <see cref="RepoInfoProvider{T}"/>.
/// </summary>
public abstract class WorldService
{
    readonly World _world;

    protected WorldService( World world )
    {
        _world = world;
    }

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World => _world;
}

/// <summary>
/// Simple Template Method Pattern that caches information associated to a <see cref="Repo"/>.
/// </summary>
/// <typeparam name="T">The information type.</typeparam>
public abstract class RepoInfoProvider<T> : WorldService
    where T : RepoInfo
{
    readonly T?[] _infos;
    ImmutableArray<T> _all;

    /// <summary>
    /// Initializes a new information cache.
    /// </summary>
    /// <param name="world">The world.</param>
    protected RepoInfoProvider( World world )
        : base( world )
    {
        _infos = new T[world.Layout.Count];
    }

    /// <summary>
    /// Gets the associated information.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="r">The Repo from which a <typeparamref name="T"/> muts be obtained.</param>
    /// <returns>The information.</returns>
    public T Get( IActivityMonitor monitor, Repo r )
    {
        var info = _infos[r.Index];
        if( info == null )
        {
            info = Create( monitor, r );
            _infos[r.Index] = info;
        }
        return info;
    }

    /// <summary>
    /// Tries to get the associated information for all the Repos.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="all">All the information on success.</param>
    /// <returns>True on success, false on error.</returns>
    public bool TryGetAll( IActivityMonitor monitor, out ImmutableArray<T> all )
    {
        all = _all;
        if( all.IsDefault )
        {
            var allRepo = World.GetAllDefinedRepo( monitor );
            if( allRepo == null ) return false;
            for( int i = 0; i < allRepo.Count; ++i )
            {
                if( _infos[i] == null )
                {
                    _infos[i] = Create( monitor, allRepo[i] );
                }
            }
            all = ImmutableCollectionsMarshal.AsImmutableArray( _infos )!;
        }
        return true;
    }

    /// <summary>
    /// Creates a <typeparamref name="T"/> for a repository.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The Repo.</param>
    /// <returns>
    /// The information.
    /// Errors may not be logged: <see cref="RepoInfo.ErrorReason"/> can capture enough error information.
    /// </returns>
    protected abstract T Create( IActivityMonitor monitor, Repo repo );
}
