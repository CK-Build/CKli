using CK.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml.Linq;

namespace CKli.Core;

sealed partial class ReflectionPluginCollector
{
    sealed class PluginType : IPluginTypeInfo
    {
        readonly PluginInfo _pluginInfo;
        readonly string _typeName;
        readonly ConstructorInfo _ctor;
        readonly object?[] _arguments;
        readonly XElement? _xmlConfig;

        readonly int[] _deps;
        readonly int _worldParameterIndex;
        readonly int _primaryPluginParameterIndex;
        readonly PluginStatus _status;
        readonly int _activationIndex;

        public PluginType( PluginInfo pluginInfo,
                           string typeName,
                           ConstructorInfo ctor,
                           int[] deps,
                           object?[] arguments,
                           XElement? xmlConfig,
                           int worldParameterIndex,
                           int primaryPluginParameterIndex,
                           PluginStatus status,
                           int activationIndex )
        {
            _pluginInfo = pluginInfo;
            _typeName = typeName;
            _ctor = ctor;
            _deps = deps;
            _arguments = arguments;
            _xmlConfig = xmlConfig;
            _worldParameterIndex = worldParameterIndex;
            _primaryPluginParameterIndex = primaryPluginParameterIndex;
            _status = status;
            _activationIndex = activationIndex;
            ((List<IPluginTypeInfo>)pluginInfo.PluginTypes).Add( this );
            Throw.DebugAssert( IsDisabled == activationIndex < 0 );
            Throw.DebugAssert( "Config is never null when the plugin is enabled.", IsDisabled || _xmlConfig != null );
        }

        public PluginInfo Plugin => _pluginInfo;

        public string TypeName => _typeName;

        public PluginStatus Status => _status;

        public bool IsPrimary => _primaryPluginParameterIndex >= 0;

        [MemberNotNullWhen( false, nameof( _xmlConfig ) )]
        public bool IsDisabled => _status.IsDisabled();

        public int ActivationIndex => _activationIndex;

        public int[] Deps => _deps;

        public int WorldParameterIndex => _worldParameterIndex;

        public int PrimaryPluginParameterIndex => _primaryPluginParameterIndex;

        internal object Instantiate( World world, object[] instantiated )
        {
            Throw.DebugAssert( !IsDisabled );
            for( int i = 0; i < _deps.Length; i++ )
            {
                int iInstance = _deps[ i ];
                if( iInstance == -1 )
                {
                    if( i == _worldParameterIndex )
                    {
                        _arguments[i] = world;
                    }
                    else if( i == _primaryPluginParameterIndex )
                    {
                        _arguments[i] = new PrimaryPluginContext( _pluginInfo, _xmlConfig, world );
                    }
                }
                else
                {
                    _arguments[i] = instantiated[i];
                }
            }
            return _ctor.Invoke( _arguments );
        }

        public override string ToString() => TypeName;
    }

}

