using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace CKli.Core;

sealed class PluginLoadContext : AssemblyLoadContext, IWorldPlugins
{
    IWorldPlugins? _worldPlugins;
    bool _disposed;

    public PluginLoadContext( WorldName worldName )
        : base( worldName.FullName, isCollectible: true )
    {
    }

    public IReadOnlyCollection<IWorldPluginInfo> Plugins => _worldPlugins!.Plugins;

    public IDisposable Create( World world ) => _worldPlugins!.Create( world );

    public void Dispose()
    {
        if( !_disposed )
        {
            _disposed = true;
            _worldPlugins?.Dispose();
            Unload();
        }
    }

    protected override Assembly? Load( AssemblyName assemblyName )
    {
        return Default.Assemblies.FirstOrDefault( x => x.FullName == assemblyName.FullName );
    }

    static public IWorldPlugins? Load( IActivityMonitor monitor, NormalizedPath dllPath, PluginCollectorContext context )
    {
        var result = new PluginLoadContext( context.WorldName );
        try
        {
            if( result.DoLoad( monitor, dllPath, context ) )
            {
                return result;
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"Error while collecting plugins.", ex );
        }
        result.Dispose();
        return null;
    }

    bool DoLoad( IActivityMonitor monitor, NormalizedPath dllPath, PluginCollectorContext context )
    {
        var a = LoadFromAssemblyPath( dllPath );
        var t = a.GetType( "CKli.Plugins.Plugins", throwOnError: false );
        if( t == null )
        {
            monitor.Error( $"Unable to find required type 'CKli.Plugins.Plugins' in {dllPath}." );
            return false;
        }
        var m = t.GetMethod( "Register", BindingFlags.Public | BindingFlags.Static, [typeof( PluginCollectorContext )] );
        if( m == null || m.ReturnType != typeof( IWorldPlugins ) )
        {
            monitor.Error( $"Unable to find method 'static IWorldPlugins Register( PluginCollectorContext ) in type 'CKli.Plugins.Plugins' of {dllPath}." );
            return false;
        }
        var r = m.Invoke( null, [context] );
        if( r is not IWorldPlugins p )
        {
            monitor.Error( $"Failed CKli.Plugins.Plugins.Register in {dllPath}." );
            return false;
        }
        _worldPlugins = p;
        return true;
    }
}

