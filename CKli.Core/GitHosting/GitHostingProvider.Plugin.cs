using CK.Core;
using CKli.Core.GitHosting.Providers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

public abstract partial class GitHostingProvider
{
    static IInternalPluginGitHostingProvider? _firstHostingProvider;
    static IInternalPluginGitHostingProvider? _lastHostingProvider;
    static List<IPluginGitHostingResolver>? _pluginResolvers;

    static internal void RegisterPluginResolver( IPluginGitHostingResolver resolver )
    {
        _pluginResolvers ??= new List<IPluginGitHostingResolver>();
        _pluginResolvers.Add( resolver );
    }

    static internal void Register( IInternalPluginGitHostingProvider provider,
                                   out IInternalPluginGitHostingProvider? next,
                                   out IInternalPluginGitHostingProvider? prev )
    {
        if( _firstHostingProvider == null )
        {
            Throw.DebugAssert( _lastHostingProvider == null );
            _firstHostingProvider = provider;
            _lastHostingProvider = provider;
            next = null;
            prev = null;
        }
        else
        {
            next = _firstHostingProvider;
            prev = null;
            _firstHostingProvider = provider;
        }
    }

    static internal void Unregister( IInternalPluginGitHostingProvider provider )
    {
        Throw.DebugAssert( _providers != null );
        var gitKey = provider.GitKey;
        _providers.Remove( (gitKey.PrefixPAT, gitKey.IsPublic) );

        if( _firstHostingProvider == provider ) _firstHostingProvider = provider.Next;
        else provider.Prev!.Next = provider.Next;
        if( _lastHostingProvider == provider ) _lastHostingProvider = provider.Prev;
        else provider.Next!.Prev = provider.Prev;
    }

    internal static void ReleaseAllPluginProviders( IActivityMonitor monitor )
    {
        _pluginResolvers?.Clear();
        while( _firstHostingProvider != null )
        {
            var toDispose = _firstHostingProvider;
            try
            {
                toDispose.Dispose();
            }
            catch( Exception ex )
            {
                monitor.Warn( $"While disposing PluginGitHostingProvider '{toDispose}'.", ex );
            }
        }
    }
}
