using CK.Core;

namespace CKli.Core;

/// <summary>
/// Simple Template Method Pattern that caches information associated to a <see cref="Repo"/>.
/// </summary>
/// <typeparam name="T">The information type.</typeparam>
public abstract class RepoInfoProvider<T> where T : RepoInfo
{
    readonly T?[] _infos;
    readonly World _world;

    /// <summary>
    /// Initializes a new information cache.
    /// </summary>
    /// <param name="world">The world.</param>
    protected RepoInfoProvider( World world )
    {
        _infos = new T[world.Layout.Count];
        _world = world;
    }

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World => _world;

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
