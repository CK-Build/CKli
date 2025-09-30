using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace CKli.Core;

sealed partial class ReflectionPluginCollector : IPluginCollector
{
    sealed record InitialReg( PluginInfo Plugin,
                              string TypeName,
                              ConstructorInfo Ctor,
                              ParameterInfo[] Parameters,
                              int PrimaryPluginParameterIndex,
                              int WorldParameterIndex,
                              XElement? Config )
    {
        public bool IsPrimary => PrimaryPluginParameterIndex >= 0;
    }

    readonly PluginCollectorContext _context;
    readonly Dictionary<Type, InitialReg> _initialCollector;
    readonly ImmutableArray<PluginInfo>.Builder _pluginInfos;
    readonly CommandCollector _commandCollector;

    public ReflectionPluginCollector( PluginCollectorContext context )
    {
        _context = context;
        _initialCollector = new Dictionary<Type, InitialReg>();
        _pluginInfos = ImmutableArray.CreateBuilder<PluginInfo>();
        _commandCollector = new CommandCollector();
    }

    public IPluginCollection BuildPluginCollection( ReadOnlySpan<Type> defaultPrimaryPlugins )
    {
        foreach( var primaryPlugin in defaultPrimaryPlugins )
        {
            var a = primaryPlugin.Assembly;
            var aName = a.GetName().Name;
            var nsPrimary = primaryPlugin.Namespace;
            if( !PluginMachinery.EnsureFullPluginName( aName, out var shortPluginName, out var fullPluginName ) )
            {
                Throw.CKException( $"Invalid Plugin assembly '{aName}' name. Name must be like 'CKli.XXX.Plugin'." );
            }
            if( aName != primaryPlugin.Namespace )
            {
                Throw.CKException( $"Invalid primary Plugin namespace '{primaryPlugin.Name}': it must be the same as the assembly name '{aName}'." );
            }
            var status = PluginStatus.Available;
            XElement? configuration = null;
            if( _context.PluginsConfiguration.TryGetValue( shortPluginName, out var config ) )
            {
                if( config.IsDisabled ) status |= PluginStatus.DisabledByConfiguration;
                else
                {
                    configuration = config.Config;
                }
            }
            else
            {
                status |= PluginStatus.DisabledByMissingConfiguration;
            }
            // The PluginType ctor (in Build) downcasts the pluginTypes list to add itself to its pluginInfo.PluginTypes.
            var pluginInfo = new PluginInfo( fullPluginName, shortPluginName, status, new List<IPluginTypeInfo>() );
            _pluginInfos.Add( pluginInfo );
            foreach( var t in a.GetExportedTypes().Where( t => !t.IsAbstract
                                                               && !t.IsGenericTypeDefinition
                                                               && typeof( PluginBase ).IsAssignableFrom( t ) ) )
            {
                var typeName = t.FullName;
                if( typeName != null )
                {
                    AddPluginType( pluginInfo, t, typeName, configuration );
                }
            }
        }
        return Build();
    }

    void AddPluginType( PluginInfo pluginInfo,
                        Type type,
                        string typeName,
                        XElement? configuration )
    {
        if( !type.IsSealed || type.IsNested )
        {
            Throw.CKException( $"Registered Plugin '{typeName}' must be a non nested sealed class." );
        }
        var ctors = type.GetConstructors();
        if( ctors.Length != 1 )
        {
            Throw.CKException( $"Plugin '{typeName}' must have a single public constructor (found {ctors.Length})." );
        }
        var ctor = ctors[0];
        var parameters = ctor.GetParameters();
        int worldParameterIndex = -1;
        int primaryPluginParameterIndex = -1;
        foreach( var p in parameters )
        {
            Type parameterType = p.ParameterType;
            if( parameterType == typeof( World ) )
            {
                if( worldParameterIndex != -1 )
                {
                    Throw.CKException( $"Duplicate World parameter in Plugin '{typeName}' constructor ('{parameters[worldParameterIndex].Name}' and '{p.Name}')." );
                }
                worldParameterIndex = p.Position;
            }
            else if( parameterType == typeof( PrimaryPluginContext ) )
            {
                if( primaryPluginParameterIndex != -1 )
                {
                    Throw.CKException( $"Duplicate PrimaryPluginContext configuration parameter in Plugin '{typeName}' constructor ('{parameters[primaryPluginParameterIndex].Name}' and '{p.Name}')." );
                }
                primaryPluginParameterIndex = p.Position;
            }
            else if( !typeof( PluginBase ).IsAssignableFrom( parameterType ) )
            {
                Throw.CKException( $"Invalid parameter in Plugin '{typeName}' constructor: {p.Name} must be a Plugin (not a '{parameterType.Name}')." );
            }
            else if( !parameterType.IsSealed )
            {
                Throw.CKException( $"Invalid parameter in Plugin '{typeName}' constructor: {p.Name} must be a concrete Plugin (not the abstract '{parameterType.Name}')." );
            }
        }
        if( worldParameterIndex >= 0 && primaryPluginParameterIndex >= 0 )
        {
            Throw.CKException( $"""
                Plugin '{typeName}' cannot have both parameters:
                'World {parameters[worldParameterIndex].Name}' (optionally used by a Support Plugin) and
                'PrimaryPluginContext {parameters[primaryPluginParameterIndex].Name}' that denotes a Primary Plugin.
                """ );
        }
        var result = new InitialReg( pluginInfo, typeName, ctor, parameters, primaryPluginParameterIndex, worldParameterIndex, configuration );
        _initialCollector.Add( type, result );
    }


    IPluginCollection Build()
    {
        List<PluginType> activationList = new List<PluginType>();
        Dictionary<Type,PluginType> plugins = new Dictionary<Type, PluginType>( _initialCollector.Count );
        foreach( var (type, reg) in _initialCollector )
        {
            // Don't create PluginType for support plugins that are not used by any primary ones.
            if( reg.IsPrimary )
            {
                if( !plugins.TryGetValue( type, out PluginType? factory ) )
                {
                    factory = RegisterPluginType( plugins, type, reg, activationList );
                }
            }
        }
        return new Result( _pluginInfos.DrainToImmutable(), activationList, _context, _commandCollector.Commands );
    }

    PluginType RegisterPluginType( Dictionary<Type, PluginType> plugins,
                                   Type type,
                                   InitialReg reg,
                                   List<PluginType> activationList )
    {
        // Use null marker to detect cycles.
        plugins.Add( type, null! );
        var status = reg.Plugin.Status;
        var parameters = reg.Parameters;
        var deps = new int[parameters.Length];
        var arguments = new object?[parameters.Length];
        foreach( var p in parameters )
        {
            int idxParam = p.Position;
            deps[idxParam] = -1;
            if( idxParam == reg.PrimaryPluginParameterIndex || idxParam == reg.WorldParameterIndex )
            {
                continue;
            }
            Type parameterType = p.ParameterType;
            if( plugins.TryGetValue( parameterType, out var dep ) )
            {
                if( dep == null )
                {
                    Throw.CKException( $"Dependency cycle detected between Plugins: '{reg.TypeName}( {parameterType.Name} {p.Name} )', but '{parameterType.Name}' also depends on '{reg.TypeName}'." );
                }
            }
            else
            {
                if( !_initialCollector.TryGetValue( parameterType, out var regDep ) )
                {
                    // Considering that an optional parameter (p.HasDefaultValue) can be fine with an unregistered
                    // dependency doesn't seem good idea.
                    // For the moment consider that all plugin type must be registered.
                    Throw.CKException( $"Plugin '{reg.TypeName}' requires the plugin '{parameterType.Name}' that is not registered." );
                }
                dep = RegisterPluginType( plugins, parameterType, regDep, activationList );
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
                Throw.DebugAssert( deps[idxParam] == -1 );
            }
            else
            {
                Throw.DebugAssert( dep.ActivationIndex >= 0 );
                deps[idxParam] = dep.ActivationIndex;
            }
        }
        int activationIndex = status.IsDisabled() ? -1 : activationList.Count;
        var result = new PluginType( reg.Plugin,
                                     reg.TypeName,
                                     reg.Ctor,
                                     deps,
                                     arguments,
                                     reg.Config,
                                     reg.WorldParameterIndex,
                                     reg.PrimaryPluginParameterIndex,
                                     status,
                                     activationIndex );
        if( activationIndex >= 0 ) activationList.Add( result );
        plugins[type] = result;

        // Discover and collects commands.
        var members = type.GetMethods();
        foreach( var m in members )
        {
            string? path = null;
            var attributes = m.GetCustomAttributesData();
            foreach( var a in attributes )
            {
                if( a.AttributeType == typeof( FullCommandPathAttribute  ) )
                {
                    path = (string)a.ConstructorArguments[0].Value!;
                }
            }
            if( path != null )
            {
                _commandCollector.Add( result, m, path, attributes );
            }
        }

        return result;
    }
}
