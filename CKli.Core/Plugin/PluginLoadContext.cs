using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace CKli.Core;

/// <summary>
/// Implements the <see cref="World.PluginLoader"/> by loading the plugins "CKli.Plugins.dll" and all its
/// installed plugins in a collectible <see cref="AssemblyLoadContext"/>.
/// <para>
/// Any assemblies that already are in the <see cref="AssemblyLoadContext.Default"/> are used: only the
/// plugins assemblies (and their possibly shared assemblies) are in the plugins context. 
/// </para>
/// </summary>
sealed class PluginLoadContext : AssemblyLoadContext, IWorldPlugins
{
    readonly string _runFolder;
    IWorldPlugins? _worldPlugins;
    bool _disposed;

    PluginLoadContext( WorldName worldName, string runFolder )
        : base( worldName.FullName, isCollectible: true )
    {
        _runFolder = runFolder;
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
        var a = Default.Assemblies.FirstOrDefault( x => x.FullName == assemblyName.FullName );
        if( a == null )
        {
            var p = $"{_runFolder}/{assemblyName.Name}.dll";
            if( !File.Exists( p ) )
            {
                Throw.CKException( $"""
                    Unable to find '{assemblyName.Name}.dll' in '{_runFolder}'.
                    Please check that 'CKli.Plugins.csproj' has <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>.
                    """ );
            }
            a = LoadFromAssemblyPath( p );
        }
        return a;
    }

    /// <summary>
    /// Loads the core "CKli.Plugins.dll" and all its plugins.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="dllPath">The core "CKli.Plugins.dll" path.</param>
    /// <param name="context">The context (provides plugin configurations.)</param>
    /// <returns></returns>
    static public IWorldPlugins? Load( IActivityMonitor monitor, NormalizedPath dllPath, PluginCollectorContext context )
    {
        var runFolder = Path.GetDirectoryName( dllPath );
        Throw.CheckArgument( "DllPath cannot be at the root.", runFolder is not null );
        var result = new PluginLoadContext( context.WorldName, runFolder );
        try
        {
            if( result.DoLoad( monitor, dllPath, context ) )
            {
                return result;
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"Error while loading plugins.", ex );
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

