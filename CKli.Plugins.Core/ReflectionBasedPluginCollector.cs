using CK.Core;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace CKli.Core;

sealed partial class ReflectionBasedPluginCollector : IPluginCollector
{
    const WorldPluginStatus _isPrimaryStatus = (WorldPluginStatus)256;

    readonly PluginCollectorContext _context;
    readonly Dictionary<Type, (ConstructorInfo Ctor, WorldPluginStatus Status, XElement? Config)> _initialCollector;
    int _primaryCount;

    readonly HashSet<Type> _cycleDetector;
    readonly Dictionary<Type, PluginFactory> _factories;

    public ReflectionBasedPluginCollector( PluginCollectorContext context )
    {
        _context = context;
        _initialCollector = new Dictionary<Type, (ConstructorInfo Ctor, WorldPluginStatus Status, XElement? Config)>();
        _cycleDetector = new HashSet<Type>();
        _factories = new Dictionary<Type, PluginFactory>();
    }

    public void AddPrimaryPlugin<T>() where T : WorldPlugin
    {
        AddPlugin( typeof( T ), true );
        _cycleDetector.Clear();
    }

    public void AddSupportPlugin<T>() where T : WorldPlugin
    {
        AddPlugin( typeof( T ), false );
        _cycleDetector.Clear();
    }

    void AddPlugin( Type type, bool primary )
    {
        if( _initialCollector.ContainsKey( type ) )
        {
            Throw.CKException( $"WorldPlugin '{type.Name}' is already registered." );
        }
        if( !type.IsClass || !type.IsSealed )
        {
            Throw.CKException( $"Registered WorldPlugin '{type.Name}' must be a sealed class." );
        }
        var ctors = type.GetConstructors();
        if( ctors.Length != 1 )
        {
            Throw.CKException( $"WorldPlugin '{type.Name}' must have a single public constructor (found {ctors.Length})." );
        }
        var ctor = ctors[0];
        var status = WorldPluginStatus.Available;
        XElement? configuration = null;
        if( primary )
        {
            ++_primaryCount;
            status |= _isPrimaryStatus;
            if( _context.PluginsConfiguration.TryGetValue( type.Name, out var config ) )
            {
                configuration = config.Config;
                if( config.IsDisabled ) status |= WorldPluginStatus.DisabledByConfiguration;
            }
            else
            {
                status |= WorldPluginStatus.DisabledByMissingConfiguration;
            }
        }
        _initialCollector.Add( type, (ctor, status, configuration) );
    }

    public IWorldPlugins Build()
    {
        List<PluginFactory> primaryList = new List<PluginFactory>();
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
                primaryList.Add( factory );
            }
        }
        return new WorldPluginResult( primaryList, activationList );
    }

    PluginFactory AddPluginFactory( Dictionary<Type, PluginFactory> plugins,
                                    Type type,   
                                    ConstructorInfo ctor,
                                    WorldPluginStatus status,
                                    XElement? config,
                                    Dictionary<Type, (ConstructorInfo Ctor, WorldPluginStatus Status, XElement? Config)> initialCollector,
                                    List<PluginFactory> activationList )
    {
        // Use null marker to detect cycles.
        plugins.Add( type, null! );
        var parameters = ctor.GetParameters();
        var deps = new int[parameters.Length];
        var arguments = new object?[parameters.Length];
        int worldParameterIndex = -1;
        int xmlConfigIndex = -1;
        foreach( var p in parameters )
        {
            deps[p.Position] = -1;
            Type parameterType = p.ParameterType;
            if( parameterType == typeof( World ) )
            {
                if( worldParameterIndex != -1 )
                {
                    Throw.CKException( $"Duplicate World parameter in WorldPlugin '{type.Name}' constructor ('{parameters[worldParameterIndex].Name}' and '{p.Name}')." );
                }
                worldParameterIndex = p.Position;
            }
            else if( parameterType == typeof( XElement ) )
            {
                if( (status & _isPrimaryStatus) == 0 )
                {
                    Throw.CKException( $"WorldPlugin '{type.Name}' is registered as a support Plugin, it cannot have a configuration (parameter 'XElement {p.Name}' is invalid)." );
                }
                if( xmlConfigIndex != -1 )
                {
                    Throw.CKException( $"Duplicate XElement configuration parameter in WorldPlugin '{type.Name}' constructor ('{parameters[xmlConfigIndex].Name}' and '{p.Name}')." );
                }
                Throw.DebugAssert( config != null || (status & WorldPluginStatus.DisabledByMissingConfiguration) != 0 );
                arguments[xmlConfigIndex = p.Position] = config;
            }
            else if( !typeof( WorldPlugin ).IsAssignableFrom( parameterType ) )
            {
                Throw.CKException( $"Invalid parameter in WorldPlugin '{type.Name}' constructor: {p.Name} must be a WorldPlugin (not a '{parameterType.Name}')." );
            }
            else if( !parameterType.IsSealed )
            {
                Throw.CKException( $"Invalid parameter in WorldPlugin '{type.Name}' constructor: {p.Name} must be a concrete WorldPlugin (not the abstract '{parameterType.Name}')." );
            }
            else
            {
                if( plugins.TryGetValue( parameterType, out var dep ) )
                {
                    if( dep == null )
                    {
                        Throw.CKException( $"Dependenct cycle detected between WorldPlugin '{type.Name}( {parameterType.Name} {p.Name} )': '{parameterType.Name}' also depends on '{type.Name}'." );
                    }
                }
                else
                {
                    if( !_initialCollector.TryGetValue( parameterType, out var initInfo ) )
                    {
                        // Considering that an optional parameter (p.HasDefaultValue) can be fine with an unregistered dependency
                        // doesn't seem  good idea. For the moment consider that all plugin type must be registered.
                        Throw.CKException( $"WorldPlugin '{type.Name}' requires the plugin '{parameterType.Name}' that is not registered." );
                    }
                    dep = AddPluginFactory( plugins, parameterType, initInfo.Ctor, initInfo.Status, initInfo.Config, initialCollector, activationList );
                }
                // Disabled dependency: if the parameter is not optional, we are transitively disabled.
                if( dep.IsDisabled )
                {
                    if( !p.HasDefaultValue )
                    {
                        status |= WorldPluginStatus.DisabledByDependency;
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
        var result = new PluginFactory( type.Name,
                                        ctor,
                                        deps,
                                        arguments,
                                        worldParameterIndex,
                                        xmlConfigIndex,
                                        status,
                                        activationIndex );
        if( activationIndex >= 0 ) activationList.Add( result );
        plugins[type] = result;
        return result;
    }

}

