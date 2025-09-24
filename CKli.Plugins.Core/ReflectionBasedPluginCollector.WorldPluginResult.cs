using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.Core;

sealed partial class ReflectionBasedPluginCollector
{
    sealed class WorldPluginResult : IPluginCollection
    {
        readonly ImmutableArray<PluginFactory> _defaultPrimaries;
        readonly List<PluginFactory> _activationList;

        public WorldPluginResult( ImmutableArray<PluginFactory> defaultPrimaries, List<PluginFactory> activationList )
        {
            _defaultPrimaries = defaultPrimaries;
            _activationList = activationList;
        }

        public IReadOnlyCollection<IPluginInfo> Plugins => _defaultPrimaries;

        public IDisposable Create( World world )
        {
            var instantiated = new object[_activationList.Count];
            for( int i = 0; i < _activationList.Count; ++i )
            {
                instantiated[i] = _activationList[i].Instantiate( world, instantiated );
            }
            return new FinalResult( instantiated );
        }

        sealed class FinalResult : IDisposable
        {
            readonly object[] _instantiated;
            bool _disposed;

            public FinalResult( object[] instantiated )
            {
                _instantiated = instantiated;
            }

            public void Dispose()
            {
                if( !_disposed )
                {
                    _disposed = true;
                    foreach( var o in _instantiated )
                    {
                        if( o is IDisposable d ) d.Dispose();
                    }
                }
            }
        }


        // We don't have any resources that must be disposed in this
        // reflection based implementation.
        public void Dispose()
        {
        }
    }

}

