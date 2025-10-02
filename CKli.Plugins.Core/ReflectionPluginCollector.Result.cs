using CK.Core;
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
        readonly IReadOnlyCollection<CommandDescription> _commands;

        public Result( ImmutableArray<PluginInfo> pluginInfos,
                       List<PluginType> activationList,
                       PluginCollectorContext context,
                       IReadOnlyCollection<CommandDescription> commands )
        {
            _pluginInfos = pluginInfos;
            _activationList = activationList;
            _context = context;
            _commands = commands;
        }

        public IReadOnlyCollection<PluginInfo> Plugins => _pluginInfos;

        public IReadOnlyCollection<CommandDescription> Commands => _commands;

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
            GenerateCommands( b );

            b.Append( """
                    return new Generated( infos, commands );
                }
            }

            sealed class Generated : IPluginCollection
            {
                readonly PluginInfo[] _plugins;
                readonly CommandDescription[] _commands;

                internal Generated( PluginInfo[] plugins, CommandDescription[] commands )
                {
                    _plugins = plugins;
                    _commands = commands;
                }

                public IReadOnlyCollection<PluginInfo> Plugins => _plugins;

                public IReadOnlyCollection<CommandDescription> Commands => _commands;

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
                PluginType? dep = _activationList[i];
                b.Append( ' ', offset ).Append( $"objects[{i}] = new {dep.TypeName}( " );
                for( int j = 0; j < dep.Deps.Length; ++j )
                {
                    if( j > 0 ) b.Append( ", " );
                    int iInstance = dep.Deps[j];
                    if( iInstance == -1 )
                    {
                        if( j == dep.WorldParameterIndex )
                        {
                            b.Append( "world" );
                        }
                        else if( j == dep.PrimaryPluginParameterIndex )
                        {
                            int idxPlugin = _pluginInfos.IndexOf( dep.Plugin );
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

        void GenerateCommands( StringBuilder b )
        {
            int offset = 8;
            b.Append( ' ', offset ).Append( "var commands = new CommandDescription[]{" ).AppendLine();
            offset += 4;
            foreach( var c in _commands )
            {
                var typeInfo = c.PluginTypeInfo;
                Throw.DebugAssert( typeInfo != null );
                int idxPlugin = _pluginInfos.IndexOf( typeInfo.Plugin );
                Throw.DebugAssert( idxPlugin >= 0 );
                int idxType = typeInfo.Plugin.PluginTypes.IndexOf( t => t == typeInfo );

                b.Append( ' ', offset ).Append( $"new CommandDescription( infos[{idxPlugin}].PluginTypes[{idxType}]," ).AppendLine();
                int paramOffset = offset + 24;
                AppendSourceString( b.Append( ' ', paramOffset ), c.CommandPath ).Append( ',' ).AppendLine();
                AppendSourceString( b.Append( ' ', paramOffset ), c.Description ).Append( ',' ).AppendLine();
                // Arguments
                b.Append( ' ', paramOffset ).Append( "arguments: [" ).AppendLine();
                paramOffset += 4;
                foreach( var a in c.Arguments )
                {
                    AppendSourceString( b.Append( ' ', paramOffset ).Append( '(' ), a.Name ).Append( ", " );
                    AppendSourceString( b, a.Description ).Append( ")," ).AppendLine();
                }
                paramOffset -= 4;
                b.Append( ' ', paramOffset ).Append( "]," ).AppendLine();
                // Options
                b.Append( ' ', paramOffset ).Append( "options: [" ).AppendLine();
                DumpOptionsOrFlags( b, paramOffset + 4, c.Options );
                b.Append( ' ', paramOffset ).Append( "]," ).AppendLine();
                // Flags
                b.Append( ' ', paramOffset ).Append( "flags: [" ).AppendLine();
                DumpOptionsOrFlags( b, paramOffset + 4, c.Flags );
                b.Append( ' ', paramOffset ).Append( "] )," ).AppendLine();
            }
            offset -= 4;
            b.Append( ' ', offset ).Append( "};" ).AppendLine();

            void DumpOptionsOrFlags( StringBuilder b, int paramOffset, ImmutableArray<(ImmutableArray<string> Names, string Description)> optionsOrFlags )
            {
                foreach( var o in optionsOrFlags )
                {
                    b.Append( ' ', paramOffset ).Append( "([" );
                    foreach( var n in o.Names )
                    {
                        AppendSourceString( b, n ).Append( ',' );
                    }
                    b.Append( "], " );
                    AppendSourceString( b, o.Description ).Append( ")," ).AppendLine();
                }
            }
        }

        StringBuilder AppendSourceString( StringBuilder b, string s )
        {
            return b.Append( '"' ).Append( s.Replace( "\r", "\\r" ).Replace( "\n", "\\n" ).Replace( "\"", "\\\"" ) ).Append( '"' );
        }
    }

}


