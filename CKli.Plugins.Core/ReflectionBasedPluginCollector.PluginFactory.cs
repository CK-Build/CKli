using CK.Core;
using System;
using System.Reflection;

namespace CKli.Core;

sealed partial class ReflectionBasedPluginCollector
{
    sealed class PluginFactory : IWorldPluginInfo
    {
        readonly string _name;
        readonly ConstructorInfo _ctor;
        readonly int[] _deps;
        readonly object?[] _arguments;
        readonly int _worldParameterIndex;
        readonly int _xmlConfigIndex;
        readonly WorldPluginStatus _status;
        readonly int _activationIndex;

        public PluginFactory( string name,
                            ConstructorInfo ctor,
                            int[] deps,
                            object?[] arguments,
                            int worldParameterIndex,
                            int xmlConfigIndex,
                            WorldPluginStatus status,
                            int activationIndex )
        {
            _name = name;
            _ctor = ctor;
            _deps = deps;
            _arguments = arguments;
            _worldParameterIndex = worldParameterIndex;
            _xmlConfigIndex = xmlConfigIndex;
            _status = status;
            _activationIndex = activationIndex;
            Throw.DebugAssert( IsDisabled == activationIndex < 0 );
        }

        public string Name => _name;

        public WorldPluginStatus Status => _status & ~_isPrimaryStatus;

        public bool IsDisabled => _status.IsDisabled();

        public int ActivationIndex => _activationIndex;

        internal object Instantiate( World world, object[] instantiated )
        {
            for( int i = 0; i < _deps.Length; i++ )
            {
                int iInstance = _deps[ i ];
                if( iInstance == -1 )
                {
                    if( iInstance == _worldParameterIndex )
                    {
                        _arguments[i] = world;
                    }
                    else if( iInstance != _xmlConfigIndex )
                    {
                        _arguments[i] = null;
                    }
                }
                else
                {
                    _arguments[i] = instantiated[i];
                }
            }
            return _ctor.Invoke( _arguments );
        }
    }

}

