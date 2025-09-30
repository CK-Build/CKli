using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace CKli.Loader;

/// <summary>
/// Implements the <see cref="World.PluginLoader"/> by loading the plugins "CKli.Plugins.dll" and all its
/// installed plugins in a collectible <see cref="AssemblyLoadContext"/>.
/// <para>
/// Any assemblies that already are in the <see cref="AssemblyLoadContext.Default"/> are used: only the
/// plugins assemblies (and their possibly shared assemblies) are in the plugins context. 
/// </para>
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext, IPluginCollection
{
    readonly string _runFolder;
    IPluginCollection? _worldPlugins;
    bool _disposed;

    PluginLoadContext( WorldName worldName, string runFolder )
        : base( worldName.FullName, isCollectible: true )
    {
        // Preserves the assembly reference to CKli.Plugins.Core.
        // Without this it is trimmed and the CKli.Plugins.Core is loaded in the plugins context.
        GC.KeepAlive( typeof( CKli.Plugins.PluginCollector ) );
        _runFolder = runFolder;
    }

    IReadOnlyCollection<PluginInfo> IPluginCollection.Plugins => _worldPlugins!.Plugins;

    IReadOnlyCollection<CommandDescription> IPluginCollection.Commands => _worldPlugins!.Commands;

    IDisposable IPluginCollection.Create( IActivityMonitor monitor, World world ) => _worldPlugins!.Create( monitor, world );

    bool IPluginCollection.IsCompiledPlugins => _worldPlugins!.IsCompiledPlugins;

    string IPluginCollection.GenerateCode() => _worldPlugins!.GenerateCode();

    /// <summary>
    /// Loads the core "CKli.Plugins.dll" and all its plugins.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="dllPath">The core "CKli.Plugins.dll" path.</param>
    /// <param name="context">The context (provides plugin configurations.)</param>
    /// <returns>The plugin collection on success, null on error.</returns>
    static public IPluginCollection? Load( IActivityMonitor monitor, NormalizedPath dllPath, PluginCollectorContext context )
    {
        if( !File.Exists( dllPath ) )
        {
            monitor.Error( $"Expected compiled plugin not found: {dllPath}" );
            return null;
        }
        var runFolder = Path.GetDirectoryName( dllPath.Path );
        Throw.CheckArgument( "DllPath cannot be at the root of the file system.", runFolder != null );
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
            monitor.Error( $"Error while collecting plugins.", ex );
        }
        ((IDisposable)result).Dispose();
        return null;
    }

    void IDisposable.Dispose()
    {
        if( !_disposed )
        {
            _disposed = true;
            _worldPlugins?.Dispose();
            _worldPlugins = null;
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

    static readonly Type[] _getOrRegisterParameterTypes = [typeof( PluginCollectorContext )];

    bool DoLoad( IActivityMonitor monitor, NormalizedPath dllPath, PluginCollectorContext context )
    {
        var a = LoadFromAssemblyPath( dllPath );
        var getOrRegisterArguments = new object[] { context };
        // Tries the CompiledPlugins.
        var t = a.GetType( "CKli.Plugins.CompiledPlugins", throwOnError: false );
        if( t != null )
        {
            var mC = t.GetMethod( "Get", BindingFlags.Public | BindingFlags.Static, _getOrRegisterParameterTypes );
            if( mC != null )
            {
                var rC = mC.Invoke( null, getOrRegisterArguments );
                if( rC is IPluginCollection pC )
                {
                    _worldPlugins = pC;
                    return true;

                }
                if( rC != null )
                {
                    // Should never happen unless there's type differences between assembly load context.
                    monitor.Warn( ActivityMonitor.Tags.ToBeInvestigated, $"static CompiledPlugins.Get() returned '{rC}'. Using reflection." );
                }
                else
                {
                    monitor.Trace( "Configuration signature changed. Using reflection." );
                }
            }
            else
            {
                // Should never happen unless the code has been manually edited.
                monitor.Warn( ActivityMonitor.Tags.ToBeInvestigated, $"Compiled static CompiledPlugins.Get() method is missing in '{dllPath}'. Using reflection." );
            }
        }
        // Falls back to the reflection based Plugins.
        t = a.GetType( "CKli.Plugins.Plugins", throwOnError: false );
        if( t == null )
        {
            monitor.Error( $"Unable to find required type 'CKli.Plugins.Plugins' in '{dllPath}'." );
            return false;
        }
        var m = t.GetMethod( "Register", BindingFlags.Public | BindingFlags.Static, _getOrRegisterParameterTypes );
        if( m == null || m.ReturnType != typeof( IPluginCollection ) )
        {
            monitor.Error( $"Unable to find method 'static IPluginCollection Register( PluginCollectorContext ) in type 'CKli.Plugins.Plugins' of '{dllPath}'." );
            return false;
        }
        var r = m.Invoke( null, getOrRegisterArguments );
        if( r is not IPluginCollection p )
        {
            monitor.Error( $"Failed CKli.Plugins.Plugins.Register in '{dllPath}'." );
            return false;
        }
        _worldPlugins = p;
        return true;
    }
}

