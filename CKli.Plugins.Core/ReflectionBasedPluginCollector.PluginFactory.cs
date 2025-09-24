using CK.Core;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml.Linq;

namespace CKli.Core;

sealed partial class ReflectionBasedPluginCollector
{
    sealed class PluginFactory : IPluginInfo
    {
        readonly Type _type;
        readonly ConstructorInfo _ctor;
        readonly int[] _deps;
        readonly object?[] _arguments;
        readonly XElement? _xmlConfig;
        readonly int _worldParameterIndex;
        readonly int _primaryPluginParameterIndex;
        readonly PluginStatus _status;
        readonly int _activationIndex;

        public PluginFactory( Type type,
                              ConstructorInfo ctor,
                              int[] deps,
                              object?[] arguments,
                              XElement? xmlConfig,
                              int worldParameterIndex,
                              int primaryPluginParameterIndex,
                              PluginStatus status,
                              int activationIndex )
        {
            _type = type;
            _ctor = ctor;
            _deps = deps;
            _arguments = arguments;
            _xmlConfig = xmlConfig;
            _worldParameterIndex = worldParameterIndex;
            _primaryPluginParameterIndex = primaryPluginParameterIndex;
            _status = status;
            _activationIndex = activationIndex;
            Throw.DebugAssert( IsDisabled == activationIndex < 0 );
            Throw.DebugAssert( "Config is never null when the plugin is enabled.", IsDisabled || _xmlConfig != null );
        }

        public string TypeName => _type.Name;

        public string FullPluginName => _type.Namespace!;

        public PluginStatus Status => _status & ~(_isPrimaryStatus|_isDefaultPrimaryStatus);

        [MemberNotNullWhen( false, nameof( _xmlConfig ) )]
        public bool IsDisabled => _status.IsDisabled();

        public int ActivationIndex => _activationIndex;

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
                        _arguments[i] = new PrimaryPluginContext( this, _xmlConfig, world );
                    }
                }
                else
                {
                    _arguments[i] = instantiated[i];
                }
            }
            return _ctor.Invoke( _arguments );
        }

        public override string ToString() => FullPluginName;
    }

}

