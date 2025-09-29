using CK.Core;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace CKli.Core;

sealed partial class ReflectionPluginCollector
{
    sealed class Result : IPluginCollection
    {
        readonly ImmutableArray<PluginInfo> _pluginInfos;
        readonly List<PluginType> _activationList;
        readonly PluginCollectorContext _context;

        public Result( ImmutableArray<PluginInfo> pluginInfos, List<PluginType> activationList, PluginCollectorContext context )
        {
            _pluginInfos = pluginInfos;
            _activationList = activationList;
            _context = context;
        }

        public IReadOnlyCollection<PluginInfo> Plugins => _pluginInfos;

        public IDisposable Create( IActivityMonitor monitor, World world )
        {
            var instantiated = new object[_activationList.Count];
            for( int i = 0; i < _activationList.Count; ++i )
            {
                instantiated[i] = _activationList[i].Instantiate( world, instantiated );
            }
            return new ActivatedPlugins( instantiated );
        }

        public bool IsCompiledPlugins => false;

        public void Dispose()
        {
            // Even if no type from the plugins surface in the PluginTypes, we
            // clear the (useless) list of types.
            foreach( var plugin in _pluginInfos )
            {
                ((List<IPluginTypeInfo>)plugin.PluginTypes).Clear();
            }
        }

        public string GenerateCode()
        {
            var b = new StringBuilder( """
            using CKli.Core;
            using CK.Core;
            using System;
            using System.Collections.Generic;
                    
            namespace CKli.Plugins;
            
            public static class CompiledPlugins
            {
                static ReadOnlySpan<byte> _configSignature => [
            """ );
            foreach( var oneByte in _context.Signature ) b.Append( oneByte ).Append( ',' );
            b.Append( """
                ];

                public static IPluginCollection? Get( PluginCollectorContext ctx )
                {
                    if( !_configSignature.SequenceEqual( ctx.Signature ) ) return null;

            """ );

            GeneratePluginInfos( b );

            b.Append( """
                    return new Generated( infos );
                }
            }

            sealed class Generated : IPluginCollection
            {
                readonly PluginInfo[] _plugins;

                internal Generated( PluginInfo[] plugins )
                {
                    _plugins = plugins;
                }

                public IReadOnlyCollection<PluginInfo> Plugins => _plugins;

                public bool IsCompiledPlugins => true;

                public string GenerateCode() => throw new InvalidOperationException( "IsCompiledPlugins" );

                public IDisposable Create( IActivityMonitor monitor, World world )
                {
                    var configs = world.DefinitionFile.ReadPluginsConfiguration( monitor );
                    Throw.CheckState( "Plugins configurations have already been loaded.", configs != null );
   
            """ );
            int offset = 8;
            b.Append( ' ', offset ).Append( $"var objects = new object[{_activationList.Count}];" ).AppendLine();
            for( int i = 0; i < _activationList.Count; i++ )
            {
                PluginType? a = _activationList[i];
                b.Append( ' ', offset ).Append( $"objects[{i}] = new {a.TypeName}( " );
                for( int j = 0; j < a.Deps.Length; ++j )
                {
                    int iInstance = a.Deps[j];
                    if( iInstance == -1 )
                    {
                        if( i == a.WorldParameterIndex )
                        {
                            b.Append( "world" );
                        }
                        else if( i == a.PrimaryPluginParameterIndex )
                        {
                            int idxPlugin = _pluginInfos.IndexOf( a.Plugin );
                            b.Append( $"new PrimaryPluginContext( _plugins[{idxPlugin}], configs, world )" );
                        }
                        else
                        {
                            b.Append( "null" );
                        }
                    }
                    else
                    {
                        b.Append( '(' ).Append( _activationList[iInstance].TypeName ).Append( ")objects[" ).Append( iInstance ).Append( ']' );
                    }
                }
                b.Append( " );" ).AppendLine();
            }
            b.Append( """
                    return new ActivatedPlugins( objects );
                }

                public void Dispose() { }
            }

            """ );

            return b.ToString();
        }

        void GeneratePluginInfos( StringBuilder b )
        {
            int offset = 8;
            b.Append( ' ', offset ).Append( "var infos = new PluginInfo[]{" ).AppendLine();
            offset += 4;
            foreach( var p in _pluginInfos )
            {
                b.Append( ' ', offset )
                 .Append( $$"""new PluginInfo( "{{p.FullPluginName}}", "{{p.PluginName}}", (PluginStatus){{(int)p.Status}}, new IPluginTypeInfo[{{p.PluginTypes.Count}}] ),""" )
                 .AppendLine();
            }
            offset -= 4;
            b.Append( ' ', offset ).Append( "};" ).AppendLine();
            if( _pluginInfos.Length > 0 )
            {
                b.Append( ' ', offset ).Append( "PluginInfo plugin;" ).AppendLine()
                 .Append( ' ', offset ).Append( "IPluginTypeInfo[] types;" ).AppendLine();
                for( int i = 0; i < _pluginInfos.Length; i++ )
                {
                    PluginInfo? p = _pluginInfos[i];
                    b.Append( ' ', offset ).Append( $"plugin = infos[{i}];" ).AppendLine()
                     .Append( ' ', offset ).Append( $"types = (IPluginTypeInfo[])plugin.PluginTypes;" ).AppendLine();
                    for( int j = 0; j < p.PluginTypes.Count; j++ )
                    {
                        IPluginTypeInfo? t = p.PluginTypes[j];
                        b.Append( ' ', offset ).Append( $"""types[{j}] = new PluginTypeInfo( plugin, "{t.TypeName}", {(t.IsPrimary ? "true" : "false")}, {(int)t.Status} );""" ).AppendLine();
                    }
                }
            }
        }

    }

}


