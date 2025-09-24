using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Xml.Linq;

namespace CKli.Core;

sealed partial class ReflectionBasedPluginCollector : IPluginCollector
{
    const PluginStatus _isPrimaryStatus = (PluginStatus)256;
    const PluginStatus _isDefaultPrimaryStatus = (PluginStatus)512;

    readonly PluginCollectorContext _context;
    readonly Dictionary<Type, (ConstructorInfo Ctor, PluginStatus Status, XElement? Config)> _initialCollector;
    int _defaultPrimariesCount;

    readonly HashSet<Type> _cycleDetector;
    readonly Dictionary<Type, PluginFactory> _factories;

    public ReflectionBasedPluginCollector( PluginCollectorContext context )
    {
        _context = context;
        _initialCollector = new Dictionary<Type, (ConstructorInfo Ctor, PluginStatus Status, XElement? Config)>();
        _cycleDetector = new HashSet<Type>();
        _factories = new Dictionary<Type, PluginFactory>();
    }

    public void AddPrimaryPlugin<T>() where T : PluginBase
    {
        AddPlugin( typeof( T ), true );
        _cycleDetector.Clear();
    }

    public void AddSupportPlugin<T>() where T : PluginBase
    {
        AddPlugin( typeof( T ), false );
        _cycleDetector.Clear();
    }

    void AddPlugin( Type type, bool primary )
    {
        if( _initialCollector.ContainsKey( type ) )
        {
            Throw.CKException( $"Plugin '{type.Name}' is already registered." );
        }
        if( !type.IsClass || !type.IsSealed )
        {
            Throw.CKException( $"Registered Plugin '{type.Name}' must be a sealed class." );
        }
        var ctors = type.GetConstructors();
        if( ctors.Length != 1 )
        {
            Throw.CKException( $"Plugin '{type.Name}' must have a single public constructor (found {ctors.Length})." );
        }
        var ctor = ctors[0];
        var status = PluginStatus.Available;
        XElement? configuration = null;
        if( primary )
        {
            string defaultPrimaryName = CheckPrimaryTypeNameAndNamespace( type );
            status |= _isPrimaryStatus;
            if( type.Name.Length == defaultPrimaryName.Length + 6 && type.Name.StartsWith( defaultPrimaryName ) )
            {
                ++_defaultPrimariesCount;
                status |= _isDefaultPrimaryStatus;
            }
            if( _context.PluginsConfiguration.TryGetValue( defaultPrimaryName, out var config ) )
            {
                configuration = config.Config;
                if( config.IsDisabled ) status |= PluginStatus.DisabledByConfiguration;
            }
            else
            {
                status |= PluginStatus.DisabledByMissingConfiguration;
            }
        }
        _initialCollector.Add( type, (ctor, status, configuration) );
    }

    private static string CheckPrimaryTypeNameAndNamespace( Type type )
    {
        var ns = type.Namespace;
        if( !PluginMachinery.IsValidFullPluginName( ns ) )
        {
            Throw.CKException( $"Invalid primary plugin namespace '{ns}': a primary plugin must be in a 'CKli.XXX.Plugin' namespace. Type: {type}." );
        }
        var assemblyName = type.Assembly.FullName;
        Throw.Assert( assemblyName != null );

        // Don't use GetName().
        if( assemblyName.Length < ns.Length
            || !assemblyName.StartsWith( ns )
            || assemblyName[ns.Length] != ',' )
        {
            Throw.CKException( $"Invalid primary plugin namespace '{ns}': the namespace must be the assembly name '{type.Assembly.GetName().Name}'. Type: {type}." );
        }
        if( !type.Name.EndsWith( "Plugin" ) )
        {
            Throw.CKException( $"Invalid primary plugin type name '{type.Name}': the type name must end with 'Plugin'. Type: {type}." );
        }
        Throw.DebugAssert( "CKli.".Length == 5 && ".Plugin".Length == 7 );
        var defaultPrimaryName = ns[5..^7];
        return defaultPrimaryName;
    }

    public IPluginCollection Build()
    {
        var defaultPrimaries = ImmutableArray.CreateBuilder<PluginFactory>( _defaultPrimariesCount );
        List<PluginFactory> activationList = new List<PluginFactory>();
        Dictionary<Type,PluginFactory> plugins = new Dictionary<Type, PluginFactory>( _initialCollector.Count );
        foreach( var (type, (ctor, status, config)) in _initialCollector )
        {
            // Don't create PluginFactory for support plugins that are not used by any primary ones.
            if( (status & _isPrimaryStatus) != 0 )
            {
                if( !plugins.TryGetValue( type, out PluginFactory? factory ) )
                {
                    factory = AddPluginFactory( plugins, type, ctor, status, config, _initialCollector, activationList );
                }
                if( (status & _isDefaultPrimaryStatus) != 0 )
                {
                    defaultPrimaries.Add( factory );
                }
            }
        }
        Throw.DebugAssert( defaultPrimaries.Count == _defaultPrimariesCount );
        return new WorldPluginResult( defaultPrimaries.MoveToImmutable(), activationList );
    }

    PluginFactory AddPluginFactory( Dictionary<Type, PluginFactory> plugins,
                                    Type type,   
                                    ConstructorInfo ctor,
                                    PluginStatus status,
                                    XElement? config,
                                    Dictionary<Type, (ConstructorInfo Ctor, PluginStatus Status, XElement? Config)> initialCollector,
                                    List<PluginFactory> activationList )
    {
        // Use null marker to detect cycles.
        plugins.Add( type, null! );
        var parameters = ctor.GetParameters();
        var deps = new int[parameters.Length];
        var arguments = new object?[parameters.Length];
        int worldParameterIndex = -1;
        int primaryPluginParameterIndex = -1;
        foreach( var p in parameters )
        {
            deps[p.Position] = -1;
            Type parameterType = p.ParameterType;
            if( parameterType == typeof( World ) )
            {
                if( worldParameterIndex != -1 )
                {
                    Throw.CKException( $"Duplicate World parameter in Plugin '{type.Name}' constructor ('{parameters[worldParameterIndex].Name}' and '{p.Name}')." );
                }
                worldParameterIndex = p.Position;
            }
            else if( parameterType == typeof( IPrimaryPluginContext ) )
            {
                if( (status & _isPrimaryStatus) == 0 )
                {
                    Throw.CKException( $"Plugin '{type.Name}' is registered as a support Plugin, parameter 'IPrimaryPluginContext {p.Name}' is invalid." );
                }
                if( primaryPluginParameterIndex != -1 )
                {
                    Throw.CKException( $"Duplicate IPrimaryPluginContext configuration parameter in Plugin '{type.Name}' constructor ('{parameters[primaryPluginParameterIndex].Name}' and '{p.Name}')." );
                }
                Throw.DebugAssert( config != null || (status & PluginStatus.DisabledByMissingConfiguration) != 0 );
                primaryPluginParameterIndex = p.Position;
            }
            else if( !typeof( PluginBase ).IsAssignableFrom( parameterType ) )
            {
                Throw.CKException( $"Invalid parameter in Plugin '{type.Name}' constructor: {p.Name} must be a Plugin (not a '{parameterType.Name}')." );
            }
            else if( !parameterType.IsSealed )
            {
                Throw.CKException( $"Invalid parameter in Plugin '{type.Name}' constructor: {p.Name} must be a concrete Plugin (not the abstract '{parameterType.Name}')." );
            }
            else
            {
                if( plugins.TryGetValue( parameterType, out var dep ) )
                {
                    if( dep == null )
                    {
                        Throw.CKException( $"Dependenct cycle detected between Plugins: '{type.Name}( {parameterType.Name} {p.Name} )', but '{parameterType.Name}' also depends on '{type.Name}'." );
                    }
                }
                else
                {
                    if( !_initialCollector.TryGetValue( parameterType, out var initInfo ) )
                    {
                        // Considering that an optional parameter (p.HasDefaultValue) can be fine with an unregistered dependency
                        // doesn't seem  good idea. For the moment consider that all plugin type must be registered.
                        Throw.CKException( $"Plugin '{type.Name}' requires the plugin '{parameterType.Name}' that is not registered." );
                    }
                    dep = AddPluginFactory( plugins, parameterType, initInfo.Ctor, initInfo.Status, initInfo.Config, initialCollector, activationList );
                }
                // Disabled dependency: if the parameter is not optional, we are transitively disabled.
                if( dep.IsDisabled )
                {
                    if( !p.HasDefaultValue )
                    {
                        status |= PluginStatus.DisabledByDependency;
                    }
                    // Let the dep index be -1: this is not used if we are disabled
                    // and -1 will set a null argument if we are not disabled and
                    // the parameter is optional.
                    Throw.DebugAssert( deps[p.Position] == -1 );
                }
                else
                {
                    Throw.DebugAssert( dep.ActivationIndex >= 0 );
                    deps[p.Position] = dep.ActivationIndex;
                }
            }
        }

        int activationIndex = status.IsDisabled() ? -1 : activationList.Count;
        var result = new PluginFactory( type,
                                        ctor,
                                        deps,
                                        arguments,
                                        config,
                                        worldParameterIndex,
                                        primaryPluginParameterIndex,
                                        status,
                                        activationIndex );
        if( activationIndex >= 0 ) activationList.Add( result );
        plugins[type] = result;
        return result;
    }
}

