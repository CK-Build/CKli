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
/// The assemblies of the shared surface (see <see cref="GetSharedAssemblies"/>) are the host ones: the plugins
/// assemblies and their own dependencies are loaded in this context. The .NET framework assemblies are not in
/// the plugins run folder: they are resolved by the host context (see <see cref="Load(AssemblyName)"/>).
/// </para>
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext, IPluginFactory
{
    readonly string _runFolder;
    IPluginFactory? _pluginFactory;
    bool _disposed;

    static Dictionary<string, Assembly>? _assemblies;

    /// <summary>
    /// Loads the assemblies of the shared surface (see <see cref="GetSharedAssemblies"/>) and registers them:
    /// <see cref="Load(AssemblyName)"/> uses them instead of the ones of the plugins run folder.
    /// <para>
    /// The host load context needs not be known: each shared assembly is obtained from one of its types, so it
    /// comes from the context into which this CKli.Loader assembly has been loaded. This matters because the
    /// <see cref="AssemblyLoadContext.Default"/> is not always the host context: NUnit3TestAdapter v6.0.0 for
    /// instance loaded the test assemblies in a dedicated "TestAssemblyLoadContext" (this was not the case in
    /// v5.2.1, nor anymore in v6.1.0).
    /// </para>
    /// <para>
    /// Must be called only once, before the first call to <see cref="Load(IActivityMonitor, NormalizedPath, PluginCollectorContext, out bool, out WeakReference?)"/>
    /// that calls it if it has not been called yet.
    /// </para>
    /// </summary>
    public static void Initialize()
    {
        Throw.CheckState( "Must be called only once.", _assemblies == null );

        var shared = GetSharedAssemblies();
        _assemblies = new Dictionary<string, Assembly>( shared.Length );
        foreach( var a in shared )
        {
            var n = a.GetName().Name;
            if( n != null ) _assemblies.TryAdd( n, a );
        }
#if DEBUG
        Throw.DebugAssert( "GetSharedAssemblies() is the CKli.Plugins.Core references closure.", CheckSharedAssemblies() );
#endif
    }

    /// <summary>
    /// The shared surface: these assemblies MUST be the host ones, they must never be loaded in a plugins
    /// context. This is the transitive closure of the assemblies referenced by CKli.Plugins.Core (the plugins
    /// contract), without the .NET framework assemblies: those are not in the plugins run folder, so
    /// <see cref="Load(AssemblyName)"/> lets the host context resolve them.
    /// <para>
    /// A type of each assembly is referenced here: this loads them (an assembly is loaded on first use only)
    /// but this also secures the assembly references of this CKli.Loader assembly. Without the CKli.Plugins.Core
    /// one for instance, the C# compiler doesn't emit the reference and CKli.Plugins.Core is loaded in the
    /// plugins context (this is also why CKli.Testing must reference it).
    /// </para>
    /// <para>
    /// This list is hard coded because assemblies are loaded lazily, on first use: considering the assemblies that
    /// the host has already loaded would make the sharing depend on the execution. This is what happened with
    /// CK.PerfectEvent: World.Events (the only user of PerfectEventSender) is created AFTER the plugins have been
    /// loaded, so the plugins ended up with their own PerfectEvent&lt;&gt; types and the first plugin that
    /// subscribed to World.Events.RepoAdded failed with "Method not found:
    /// 'CK.PerfectEvent.PerfectEvent`1&lt;CKli.Core.RepoAddedEventArgs&gt; CKli.Core.WorldEvents.get_RepoAdded()'".
    /// </para>
    /// <para>
    /// CheckSharedAssemblies() below asserts in DEBUG that this list is exactly the closure: any new dependency
    /// of CKli.Core or CKli.Plugins.Core must appear here.
    /// </para>
    /// </summary>
    static Assembly[] GetSharedAssemblies() =>
    [
        typeof( CKli.Plugins.PluginCollector ).Assembly,                                            // CKli.Plugins.Core
        typeof( World ).Assembly,                                                                   // CKli.Core
        typeof( CK.PerfectEvent.PerfectEventSender<> ).Assembly,                                    // CK.PerfectEvent
        typeof( NormalizedPath ).Assembly,                                                          // CK.Core
        typeof( ActivityMonitor ).Assembly,                                                         // CK.ActivityMonitor
        typeof( ActivityMonitorSimpleSenderExtension ).Assembly,                                    // CK.ActivityMonitor.SimpleSender
        typeof( SVersion ).Assembly,                                                                // CK.SVersion
        typeof( CK.Monitoring.GrandOutput ).Assembly,                                               // CK.Monitoring
        typeof( LibGit2Sharp.Repository ).Assembly,                                                 // LibGit2Sharp
        typeof( CommunityToolkit.HighPerformance.ArrayExtensions ).Assembly,                         // CommunityToolkit.HighPerformance
        typeof( Microsoft.IO.RecyclableMemoryStreamManager ).Assembly,                              // Microsoft.IO.RecyclableMemoryStream
        typeof( Microsoft.Extensions.Configuration.ConfigurationBuilder ).Assembly,                  // Microsoft.Extensions.Configuration
        typeof( Microsoft.Extensions.Configuration.IConfiguration ).Assembly,                        // ...Configuration.Abstractions
        typeof( Microsoft.Extensions.Configuration.FileConfigurationSource ).Assembly,               // ...Configuration.FileExtensions
        typeof( Microsoft.Extensions.Configuration.Json.JsonConfigurationSource ).Assembly,          // ...Configuration.Json
        typeof( Microsoft.Extensions.Configuration.UserSecretsConfigurationExtensions ).Assembly,    // ...Configuration.UserSecrets
        typeof( Microsoft.Extensions.DependencyInjection.ActivatorUtilities ).Assembly,              // ...DependencyInjection.Abstractions
        typeof( Microsoft.Extensions.FileProviders.IFileProvider ).Assembly,                         // ...FileProviders.Abstractions
        typeof( Microsoft.Extensions.FileProviders.PhysicalFileProvider ).Assembly,                  // ...FileProviders.Physical
        typeof( Microsoft.Extensions.FileSystemGlobbing.FilePatternMatch ).Assembly,                 // ...FileSystemGlobbing
        typeof( Microsoft.Extensions.Primitives.IChangeToken ).Assembly                              // ...Primitives
    ];

#if DEBUG
    // Computes the transitive closure of the assemblies referenced by CKli.Plugins.Core and checks that
    // GetSharedAssemblies() is this closure minus the .NET framework assemblies.
    // This walk loads the whole closure (and its framework part is by far the biggest one): this is why it
    // is done in DEBUG only. Throw.DebugAssert is [Conditional( "DEBUG" )]: in Release, the call itself
    // (and this computation) is not even emitted.
    static bool CheckSharedAssemblies()
    {
        var frameworkFolder = Path.GetDirectoryName( typeof( object ).Assembly.Location );
        var closure = new HashSet<string>();
        var visited = new HashSet<string>();
        var pending = new Stack<Assembly>();
        pending.Push( typeof( CKli.Plugins.PluginCollector ).Assembly );
        while( pending.TryPop( out var a ) )
        {
            var name = a.GetName().Name;
            if( name == null || !visited.Add( name ) ) continue;
            if( Path.GetDirectoryName( a.Location ) != frameworkFolder ) closure.Add( name );
            foreach( var r in a.GetReferencedAssemblies() )
            {
                if( r.Name == null || visited.Contains( r.Name ) ) continue;
                try
                {
                    pending.Push( Assembly.Load( r ) );
                }
                catch( Exception ex )
                {
                    visited.Add( r.Name );
                    ActivityMonitor.StaticLogger.Warn( $"Unable to load '{r}' referenced by '{name}'.", ex );
                }
            }
        }
        var declared = new HashSet<string>();
        foreach( var a in GetSharedAssemblies() )
        {
            var n = a.GetName().Name;
            if( n != null ) declared.Add( n );
        }
        if( closure.SetEquals( declared ) ) return true;
        var missing = new HashSet<string>( closure );
        missing.ExceptWith( declared );
        var useless = new HashSet<string>( declared );
        useless.ExceptWith( closure );
        ActivityMonitor.StaticLogger.Error( $"Invalid GetSharedAssemblies(). Missing: {string.Join( ", ", missing )}. Useless: {string.Join( ", ", useless )}." );
        return false;
    }
#endif

    /// <summary>
    /// Gets whether <see cref="Initialize()"/> has been called.
    /// </summary>
    public static bool IsInitialized => _assemblies != null;

    PluginLoadContext( WorldName worldName, string runFolder )
        : base( worldName.FullName, isCollectible: true )
    {
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
        if( _assemblies.TryGetValue( assemblyName.Name, out var a ) )
        {
            return a;
        }
        var p = $"{_runFolder}/{assemblyName.Name}.dll";
        if( File.Exists( p ) )
        {
            return LoadFromAssemblyPath( p );
        }
        // Not shared and not in the run folder: this is the regular case of the .NET framework assemblies
        // (they are not copied in the run folder), the host context resolves them. If it cannot, then the
        // run folder is incomplete.
        try
        {
            return Assembly.Load( assemblyName );
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( $"""
                Unable to find '{assemblyName.Name}.dll' in '{_runFolder}' and the host context cannot load it.
                Please check that 'CKli.Plugins.csproj' has <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>.
                """, ex );
            throw;
        }
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

