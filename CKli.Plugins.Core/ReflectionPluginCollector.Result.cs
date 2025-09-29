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

        public Result( ImmutableArray<PluginInfo> pluginInfos, List<PluginType> activationList )
        {
            _pluginInfos = pluginInfos;
            _activationList = activationList;
        }

        public IReadOnlyCollection<PluginInfo> Plugins => _pluginInfos;

        public IDisposable Create( World world )
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
                  
            namespace CKli.Plugins;
            
            public static class CompiledPlugins
            {
                sealed class T : IPluginTypeInfo
                {
                    public T( PluginInfo plugin, string typeName, bool isPrimary, int status )
                    {
                        Plugin = plugin;
                        TypeName = typeName;
                        IsPrimary = isPrimary;
                        Status = (PluginStatus)status;
                    }

                    public PluginInfo Plugin { get; }

                    public string TypeName { get; }

                    public bool IsPrimary { get; }

                    public PluginStatus Status { get; }

                    public override string ToString() => TypeName;
                }

                public static IPluginCollection Get( PluginCollectorContext ctx )
                {
            """ );
            int offset = 8;
            b.Append(' ',offset).Append( "var infos = new PluginInfo[{" ).AppendLine();
            offset += 4;
            foreach( var p in _pluginInfos )
            {
                b.Append( ' ', offset )
                 .Append( $$"""new PluginInfo( "{{p.FullPluginName}}", "{{p.PluginName}}", (PluginStatus)(int){{p.Status}}, new IPluginTypeInfo[{{p.PluginTypes.Count}}] ),""" )
                 .AppendLine();
            }
            offset -= 4;
            b.Append( ' ', offset ).Append( "}];" ).AppendLine()
             .Append( ' ', offset ).Append( "PluginInfo plugin;" ).AppendLine()
             .Append( ' ', offset ).Append( "IPluginTypeInfo[] types;" ).AppendLine();
            for( int i = 0; i < _pluginInfos.Length; i++ )
            {
                PluginInfo? p = _pluginInfos[i];
                b.Append( ' ', offset ).Append( $"plugin = infos[{i}];" ).AppendLine()
                 .Append( ' ', offset ).Append( $"types = (IPluginTypeInfo[])plugin.PluginTypes;" ).AppendLine();
                for( int j = 0; j < p.PluginTypes.Count; j++ )
                {
                    IPluginTypeInfo? t = p.PluginTypes[j];
                    b.Append( ' ', offset ).Append( $"""types[{j}] = new T( plugin, "{t.TypeName}", {(t.IsPrimary ? "true" : "false")}, {(int)t.Status} );""" ).AppendLine();
                }
            }
            b.Append( """
                    return new Generated( infos );
                }
            }

            sealed class Generated : IPluginCollection
            {
                public Generated( IReadOnlyCollection<PluginInfo> plugins )
                {
                    Plugins = plugins;
                }

                public IReadOnlyCollection<PluginInfo> Plugins { get; }

                public bool IsCompiledPlugins => true;

                public string GenerateCode() => throw new InvalidOperationException( "IsCompiledPlugins" );

                public IDisposable Create( World world )
                {
            
            """ );
            offset = 4;
            b.Append( ' ', offset ).Append( $"var objects = new object[{_activationList.Count}];" ).AppendLine();

            b.Append( """
                    return new ActivatedPlugins( objects );
                }

                public void Dispose() { }
            }

            """ );

            return b.ToString();
        }
        

    }

}


