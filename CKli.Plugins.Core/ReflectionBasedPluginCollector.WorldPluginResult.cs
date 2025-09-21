using System;
using System.Collections.Generic;

namespace CKli.Core;

sealed partial class ReflectionBasedPluginCollector
{
    sealed class WorldPluginResult : IWorldPlugins
    {
        readonly List<PluginFactory> _primaryList;
        readonly List<PluginFactory> _activationList;

        public WorldPluginResult( List<PluginFactory> primaryList, List<PluginFactory> activationList )
        {
            _primaryList = primaryList;
            _activationList = activationList;
        }

        public IReadOnlyCollection<IWorldPluginInfo> Plugins => _primaryList;

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

