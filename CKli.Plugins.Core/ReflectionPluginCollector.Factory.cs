using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace CKli.Core;

sealed partial class ReflectionPluginCollector
{
    sealed partial class Factory : IPluginFactory
    {
        readonly ImmutableArray<PluginInfo> _pluginInfos;
        readonly List<PluginType> _activationList;
        readonly PluginCollectorContext _context;
        readonly CommandNamespace _commands;
        readonly List<PluginCommand> _pluginCommands;

        public Factory( ImmutableArray<PluginInfo> pluginInfos,
                        List<PluginType> activationList,
                        PluginCollectorContext context,
                        CommandNamespace commands,
                        List<PluginCommand> pluginCommands )
        {
            _pluginInfos = pluginInfos;
            _activationList = activationList;
            _context = context;
            _commands = commands;
            _pluginCommands = pluginCommands;
        }

        public PluginCollection Create( IActivityMonitor monitor, World world )
        {
            var instantiated = new object[_activationList.Count];
            for( int i = 0; i < _activationList.Count; ++i )
            {
                instantiated[i] = _activationList[i].Instantiate( world, instantiated );
            }
            return PluginCollectionImpl.CreateAndBindCommands( instantiated, _pluginInfos, _commands, _pluginCommands );
        }

        public PluginCompileMode CompileMode => PluginCompileMode.None;

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
            using System.Threading.Tasks;
            using System.Collections.Generic;
                    
            namespace CKli.Plugins;
            
            public static class CompiledPlugins
            {
                static ReadOnlySpan<byte> _configSignature => [
            """ );
            foreach( var oneByte in _context.Signature ) b.Append( oneByte ).Append( ',' );
            b.Append( """
                ];

                public static IPluginFactory? Get( PluginCollectorContext ctx )
                {
                    if( !_configSignature.SequenceEqual( ctx.Signature ) ) return null;

            """ );

            GeneratePluginInfos( b );
            GeneratePluginCommandsArray( b );

            b.Append( """
                    var commandBuilder = new CommandNamespaceBuilder();
                    foreach( var c in pluginCommands )
                    {
                        commandBuilder.Add( c );
                    }
                    return new Generated( infos, pluginCommands, commandBuilder.Build() );
                }
            }

            sealed class Generated : IPluginFactory
            {
                readonly PluginInfo[] _plugins;
                readonly PluginCommand[] _pluginCommands;
                readonly CommandNamespace _commands;
            
                internal Generated( PluginInfo[] plugins, PluginCommand[] pluginCommands, CommandNamespace commands )
                {
                    _plugins = plugins;
                    _pluginCommands = pluginCommands;
                    _commands = commands;
                }
                
            #if DEBUG
                public PluginCompileMode CompileMode => PluginCompileMode.Debug;
            #else
                public PluginCompileMode CompileMode => PluginCompileMode.Release;
            #endif

                public string GenerateCode() => throw new InvalidOperationException( "CompileMode is not PluginCompileMode.None" );

                public PluginCollection Create( IActivityMonitor monitor, World world )
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
                    return PluginCollectionImpl.CreateAndBindCommands( objects, _plugins, _commands, _pluginCommands );
                }

                public void Dispose() { }
            }

            """ );

            foreach( var c in _pluginCommands )
            {
                GenerateCommand( b, c );
            }

            return b.ToString();
        }

        void GeneratePluginInfos( StringBuilder b )
        {
            int offset = 8;
            b.Append( ' ', offset ).Append( "var infos = new PluginInfo[]{" ).AppendLine();
            offset += 4;
            foreach( var p in _pluginInfos )
            {
                var version = p.InformationalVersion == null ? "null" : '"' + p.InformationalVersion.OriginalInformationalVersion + '"';
                b.Append( ' ', offset )
                 .Append( $$"""new PluginInfo( "{{p.FullPluginName}}", "{{p.PluginName}}", (PluginStatus){{(int)p.Status}}, {{version}}, new IPluginTypeInfo[{{p.PluginTypes.Count}}] ),""" )
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
                        b.Append( ' ', offset )
                         .Append( $"""types[{j}] = new PluginTypeInfo( plugin, "{t.TypeName}", {(t.IsPrimary ? "true" : "false")}, {(int)t.Status}, {t.ActivationIndex} );""" )
                         .AppendLine();
                    }
                }
            }
        }

        void GeneratePluginCommandsArray( StringBuilder b )
        {
            int offset = 8;
            b.Append( ' ', offset ).Append( "var pluginCommands = new PluginCommand[]{" ).AppendLine();
            offset += 4;
            foreach( var c in _pluginCommands )
            {
                var typeInfo = c.PluginTypeInfo;
                int idxPlugin = _pluginInfos.IndexOf( typeInfo.Plugin );
                Throw.DebugAssert( idxPlugin >= 0 );
                int idxType = typeInfo.Plugin.PluginTypes.IndexOf( t => t == typeInfo );

                b.Append( ' ', offset ).Append( "new Cmd_" );
                Command.WriteCommandPathAsIdentifier( b, c ).Append( $"( infos[{idxPlugin}].PluginTypes[{idxType}] )," ).AppendLine();
            }
            offset -= 4;
            b.Append( ' ', offset ).Append( "};" ).AppendLine();
        }

        static void GenerateCommand( StringBuilder b, PluginCommand c )
        {
            b.Append( "sealed class Cmd_" );
            Command.WriteCommandPathAsIdentifier( b, c ).Append( " : PluginCommand" ).AppendLine()
                .Append( '{' ).AppendLine();
            int offset = 4;
            b.Append( ' ', offset ).Append( "internal Cmd_" );
            Command.WriteCommandPathAsIdentifier( b, c ).Append( "( IPluginTypeInfo typeInfo )" ).AppendLine();
            offset += 4;
            b.Append( ' ', offset ).Append( ": base( typeInfo," ).AppendLine();
            offset += 8;
            AppendSourceString( b.Append( ' ', offset ), c.CommandPath ).Append( ',' ).AppendLine();
            AppendSourceString( b.Append( ' ', offset ), c.Description ).Append( ',' ).AppendLine();

            // Arguments
            b.Append( ' ', offset ).Append( "arguments: [" ).AppendLine();
            offset += 4;
            foreach( var a in c.Arguments )
            {
                AppendSourceString( b.Append( ' ', offset ).Append( '(' ), a.Name ).Append( ", " );
                AppendSourceString( b, a.Description ).Append( ")," ).AppendLine();
            }
            offset -= 4;
            b.Append( ' ', offset ).Append( "]," ).AppendLine();
            // Options
            b.Append( ' ', offset ).Append( "options: [" ).AppendLine();
            DumpOptions( b, offset + 4, c.Options );
            b.Append( ' ', offset ).Append( "]," ).AppendLine();
            // Flags
            b.Append( ' ', offset ).Append( "flags: [" ).AppendLine();
            DumpFlags( b, offset + 4, c.Flags );
            b.Append( ' ', offset ).Append( "]," ).AppendLine();
            b.Append( " ", offset ).Append( '"' ).Append( c.MethodName ).Append( "\", MethodAsyncReturn." ).Append( c.ReturnType ).Append(" ) {}").AppendLine();

            offset = 4;
            b.Append( ' ', offset ).Append( "protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )" )
                                   .AppendLine();
            b.Append( ' ', offset ).Append( '{' ).AppendLine();
            offset += 4;

            for( int i = 0; i < c.Arguments.Length; i++ )
            {
                b.Append( ' ', offset ).Append( "var a" ).Append( i ).Append( " = cmdLine.EatArgument();" ).AppendLine();
            }
            for( int i = 0; i < c.Options.Length; i++ )
            {
                b.Append( ' ', offset ).Append( "var o" ).Append( i ).Append( " = cmdLine.Eat" )
                                       .Append( c.Options[i].Multiple ? "Multiple" : "Single" )
                                       .Append( "Option( Options[" ).Append( i ).Append( "].Names );" ).AppendLine();
            }
            for( int i = 0; i < c.Flags.Length; i++ )
            {
                b.Append( ' ', offset ).Append( "var f" ).Append( i ).Append( " = cmdLine.EatFlag( Flags[" ).Append( i ).Append( "].Names );" ).AppendLine();
            }
            b.Append( ' ', offset ).Append( "if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );" ).AppendLine();

            b.Append( ' ', offset ).Append( "return " );
            switch( c.ReturnType )
            {
                case MethodAsyncReturn.None:
                    b.Append( "ValueTask.FromResult( " );
                    GenerateCall( b, offset + 35, c );
                    b.Append( " );" );
                    break;
                case MethodAsyncReturn.ValueTask:
                    GenerateCall( b, offset + 10, c );
                    break;
                default:
                    Throw.DebugAssert( c.ReturnType == MethodAsyncReturn.Task );
                    b.Append( "new ValueTask<bool>( " );
                    GenerateCall( b, offset + 35, c );
                    b.Append( " );" );
                    break;
            }
            offset -= 4;
            b.AppendLine().Append( ' ', offset ).Append( '}' ).AppendLine();

            b.Append( '}' ).AppendLine();

            static void DumpOptions( StringBuilder b, int paramOffset, ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options )
            {
                foreach( var o in options )
                {
                    b.Append( ' ', paramOffset ).Append( "([" );
                    foreach( var n in o.Names )
                    {
                        AppendSourceString( b, n ).Append( ',' );
                    }
                    b.Append( "], " );
                    AppendSourceString( b, o.Description ).Append( ", " ).Append( o.Multiple ? "true" : "false" ).Append( " )," ).AppendLine();
                }
            }

            static void DumpFlags( StringBuilder b, int paramOffset, ImmutableArray<(ImmutableArray<string> Names, string Description)> flags )
            {
                foreach( var f in flags )
                {
                    b.Append( ' ', paramOffset ).Append( "([" );
                    foreach( var n in f.Names )
                    {
                        AppendSourceString( b, n ).Append( ',' );
                    }
                    b.Append( "], " );
                    AppendSourceString( b, f.Description ).Append( " )," ).AppendLine();
                }
            }

            static void GenerateCall( StringBuilder b, int offset, PluginCommand c )
            {
                b.Append( "((" ).Append( c.PluginTypeInfo.TypeName ).Append( ")Instance)." ).Append( c.MethodName ).Append( '(' ).AppendLine();
                b.Append( ' ', offset ).Append( "monitor" );
                for( int i = 0; i < c.Arguments.Length; i++ )
                {
                    b.Append( ", a" ).Append( i );
                }
                for( int i = 0; i < c.Options.Length; i++ )
                {
                    b.Append( ", o" ).Append( i );
                }
                for( int i = 0; i < c.Flags.Length; i++ )
                {
                    b.Append( ", f" ).Append( i );
                }
                b.Append( " )" );
            }

        }

        static StringBuilder AppendSourceString( StringBuilder b, string s )
        {
            return b.Append( '"' ).Append( s.Replace( "\r", "\\r" ).Replace( "\n", "\\n" ).Replace( "\"", "\\\"" ) ).Append( '"' );
        }
    }

}


