using CK.Core;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

/// <summary>
/// <see cref="PluginBase"/> simple Template Method Pattern that can compute and cache
/// any information associated to each <see cref="Repo"/> of a Stack.
/// <para>
/// A plugin that specializes this class is "optional": it will be instantiated only if required by
/// another plugin constructor. Use the <see cref="PrimaryRepoPlugin{T}"/> as the base class for a
/// primary plugin that will be instantiated by default.
/// </para>
/// </summary>
/// <typeparam name="T">The information type.</typeparam>
public abstract class RepoPluginBase<T> : PluginBase
    where T : RepoInfo
{
    readonly T?[] _infos;
    ImmutableArray<T> _all;

    /// <summary>
    /// Initializes a new information cache.
    /// </summary>
    /// <param name="world">The world.</param>
    protected RepoPluginBase( World world )
        : base( world )
    {
        _infos = new T[world.Layout.Count];
    }

    /// <summary>
    /// Gets whether the <typeparamref name="T"/> for the provided Repo has already been created.
    /// </summary>
    /// <param name="r">The repository.</param>
    /// <returns>
    /// True if the info associated to the repository, false if the next
    /// call to <see cref="Get(IActivityMonitor, Repo)"/> will create it.
    /// </returns>
    protected bool HasRepoInfoBeenCreated( Repo r ) => _infos[r.Index] != null;

    /// <summary>
    /// Gets the associated information of a given <see cref="Repo"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="r">The Repo from which a <typeparamref name="T"/> must be obtained.</param>
    /// <returns>The information.</returns>
    public T Get( IActivityMonitor monitor, Repo r )
    {
        var info = _infos[r.Index];
        if( info == null )
        {
            using( monitor.OpenTrace( $"Creating '{typeof(T).Name}' for '{r.DisplayPath}'." ) )
            {
                info = Create( monitor, r );
                _infos[r.Index] = info;
            }
        }
        return info;
    }

    /// <summary>
    /// Gets the non null info for the repository only if <see cref="RepoInfo.HasIssue"/> is false.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="before">Expected following operation description. When null, no error is emitted (it must be emitted by the caller).</param>
    /// <returns>The non null info or null if there are issues.</returns>
    public T? GetWithoutIssue( IActivityMonitor monitor, Repo repo, string? before = "continuing" )
    {
        var versionInfo = Get( monitor, repo );
        if( versionInfo.HasIssue )
        {
            if( before != null )
            {
                monitor.Error( $"Please fix any issue before {before}." );
            }
            return null;
        }
        return versionInfo;
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
            _all = all;
        }
        return true;
    }


    /// <summary>
    /// Tries to get the associated information for all the Repos if and only if no issue exist for any of them.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="all">All the information on success.</param>
    /// <param name="before">Expected following operation description. When null, no error is emitted (it must be emitted by the caller).</param>
    /// <returns>True on success, false on error.</returns>
    public bool TryGetAllWithoutIssue( IActivityMonitor monitor, out ImmutableArray<T> all, string? before = "continuing" )
    {
        if( TryGetAll( monitor, out all ) )
        {
            foreach( var info in all )
            {
                if( info.HasIssue )
                {
                    monitor.Error( $"Please fix any issue before {before}." );
                    return false;
                }
            }
            return true;
        }
        return false;
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
