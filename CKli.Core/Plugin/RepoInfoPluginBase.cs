using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CKli.Core;

/// <summary>
/// Non generic base class for <see cref="RepoPluginBase{T}"/> and <see cref="PrimaryRepoPlugin{T}"/>.
/// </summary>
public abstract class RepoInfoPluginBase : PluginBase
{
    readonly Type _infoType;
    readonly RepoInfoPluginBase? _next;

    private protected RepoInfoPluginBase( World world, Type infoType )
    : base( world )
    {
        _infoType = infoType;
        _next = world.RegisterNextRepoInfoPlugin( this );
    }

    internal static RepoInfoPluginBase FindRepoInfoPlugin( RepoInfoPluginBase? first, Type repoInfoType )
    {
        RepoInfoPluginBase? impl = null;
        int implCount = 0;
        var n = first;
        while( n != null )
        {
            ++implCount;
            if( !Collect( repoInfoType, n, ref impl, out var error ) )
            {
                Throw.InvalidOperationException( error );
            }
            n = n._next;
        }
        if( impl == null )
        {
            Throw.InvalidOperationException( $"Unable to find a plugin implementation that exposes Repo info of type '{repoInfoType}'." );
        }
        return impl;

        static bool Collect( Type type, RepoInfoPluginBase p, ref RepoInfoPluginBase? impl, [NotNullWhen(false)]out string? error )
        {
            // Don't use p._infoType.IsAssignableFrom( type ) here.
            // The RepoPluginBase<T> is not covariant because of bool TryGetAll( IActivityMonitor monitor, out ImmutableArray<T> all ).
            // So we use equal.
            // This is inline with the simple plugin system (that doesn't support abstraction).
            if( p._infoType == type )
            {
                if( impl != null )
                {
                    error = $"Ambiguous plugin implementation: both '{impl.GetType()}' and '{p.GetType()}' expose a Repo info that is a '{type}'.";
                    return false;
                }
                impl = p;
            }
            error = null;
            return true;
        }
    }


}
