using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;
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
public sealed class PluginLoadContext : AssemblyLoadContext, IPluginFactory
{
    readonly string _runFolder;
    IPluginFactory? _pluginFactory;
    bool _disposed;

    static Dictionary<string, Assembly>? _assemblies;

    /// <summary>
    /// Depending on the host, the <see cref="AssemblyLoadContext.Default"/> may not be the load context
    /// into which the application assemblies are loaded. For instance, NUnit3TestAdapter v6.0.0 loads 
    /// the assemblies in a dedicated context (wasn't the case in v5.2.1).
    /// <para>
    /// Fortunately, <see cref="AssemblyLoadContext.All"/> exposes all the contexts so we can consider
    /// another "Default" context when needed.
    /// </para>
    /// <para>
    /// This must obviously be called before the first call to <see cref="Load(IActivityMonitor, NormalizedPath, PluginCollectorContext, out bool, out WeakReference?)"/>.
    /// If not called, only the <see cref="AssemblyLoadContext.Default"/> is considered.
    /// </para>
    /// </summary>
    /// <param name="supplementary">Optional supplementary context to consider.</param>
    public static void Initialize( AssemblyLoadContext? supplementary = null )
    {
        Throw.CheckState( "Must be called only once.", _assemblies == null );
        _assemblies = new Dictionary<string, Assembly>( 100 );
        // With NUnit contexts, there are assemblies that are in both contexts...
        // It appears that the "right" one must be the one of the NUnit context,
        // the one loaded in the default context is "hidden".
        // So we fill the final index with this in mind (but this is ad-hoc and may
        // require adaptation in the future).
        if( supplementary != null )
        {
            foreach( var a in supplementary.Assemblies )
            {
                var n = a.GetName().Name;
                if( n != null ) _assemblies.Add( n, a );
            }
        }
        foreach( var a in Default.Assemblies )
        {
            var n = a.GetName().Name;
            if( n != null ) _assemblies.TryAdd( n, a );
        }
    }

    /// <summary>
    /// Gets whether <see cref="Initialize(AssemblyLoadContext?)"/> has been called.
    /// </summary>
    public static bool IsInitialized => _assemblies != null;

    PluginLoadContext( WorldName worldName, string runFolder )
        : base( worldName.FullName, isCollectible: true )
    {
        // Preserves the assembly reference to CKli.Plugins.Core.
        // Without this it is trimmed and the CKli.Plugins.Core is loaded in the plugins context.
        GC.KeepAlive( typeof( CKli.Plugins.PluginCollector ) );
        _runFolder = runFolder;
    }

    PluginCollection IPluginFactory.Create( IActivityMonitor monitor, World world ) => _pluginFactory!.Create( monitor, world );

    PluginCompileMode IPluginFactory.CompileMode => _pluginFactory!.CompileMode;

    string IPluginFactory.GenerateCode() => _pluginFactory!.GenerateCode();

    static public IPluginFactory? Load( IActivityMonitor monitor,
                                        NormalizedPath dllPath,
                                        PluginCollectorContext context,
                                        out bool recoverableError,
                                        out WeakReference? loader )
    {
        if( _assemblies == null ) Initialize();
        recoverableError = false;
        if( !File.Exists( dllPath ) )
        {
            recoverableError = true;
            loader = null;
            monitor.Warn( $"Expected compiled plugin not found: {dllPath}" );
            return null;
        }
        var runFolder = Path.GetDirectoryName( dllPath.Path );
        Throw.CheckArgument( "DllPath cannot be at the root of the file system.", runFolder != null );
        var result = new PluginLoadContext( context.WorldName, runFolder );
        loader = new WeakReference( result, trackResurrection: true );
        try
        {
            // If this fails without throwing exceptions, this is not a recoverable error: the "regular" error cases
            // are handled and ultimately, reflection is used.
            if( result.DoLoad( monitor, dllPath, context ) )
            {
                return result;
            }
        }
        catch( Exception ex )
        {
            // An exception here indicates that a call to the plugins fails, typically
            // with a missing method exception. By deleting the CKli.CompiledPlugins.cs generated file,
            // reflection may do the job. And if it still fails, CKLi switches to "Working without plugins"
            // mode that allows plugin commands to be executed, typically to upgrade a packaged plugin.
            recoverableError = true;
            monitor.Info( $"Error while collecting plugins.", ex );
        }
        // Dispose the AssemblyLoadContext but the loader weak reference must not be alive anymore before
        // retrying to compile or obtain the plugins.
        ((IDisposable)result).Dispose();
        return null;
    }

    void IDisposable.Dispose()
    {
        if( !_disposed )
        {
            _disposed = true;
            _pluginFactory?.Dispose();
            _pluginFactory = null;
            Unload();
        }
    }

    protected override Assembly? Load( AssemblyName assemblyName )
    {
        Throw.DebugAssert( _assemblies != null );
        Throw.CheckArgument( assemblyName.Name != null );
        if( !_assemblies.TryGetValue( assemblyName.Name, out var a ) )
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
                var rC = mC.Invoke( null, BindingFlags.DoNotWrapExceptions, null, getOrRegisterArguments, System.Globalization.CultureInfo.InvariantCulture );
                if( rC is IPluginFactory pC )
                {
                    _pluginFactory = pC;
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
        if( m == null || m.ReturnType != typeof( IPluginFactory ) )
        {
            monitor.Error( $"Unable to find method 'static IPluginFactory Register( PluginCollectorContext ) in type 'CKli.Plugins.Plugins' of '{dllPath}'." );
            return false;
        }
        var r = m.Invoke( null, BindingFlags.DoNotWrapExceptions, null, getOrRegisterArguments, System.Globalization.CultureInfo.InvariantCulture );
        if( r is not IPluginFactory p )
        {
            monitor.Error( $"Call to 'CKli.Plugins.Plugins.Register' method failed to return a IPluginFactory in '{dllPath}'." );
            return false;
        }
        _pluginFactory = p;
        return true;
    }
}

