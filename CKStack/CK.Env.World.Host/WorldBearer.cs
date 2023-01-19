using CK.Core;
using CK.SimpleKeyVault;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Bears a world and manages its opening and closing.
    /// </summary>
    public sealed class WorldBearer : IDisposable
    {
        FileSystem? _fs;
        XTypedObject? _root;
        World? _world;
        readonly ICkliApplicationContext _appContext;
        readonly XTypedFactory _xFactory;

        public WorldBearer( ICkliApplicationContext appContext )
        {
            _appContext = appContext;
            _xFactory = new XTypedFactory( appContext.CoreXTypedMap );
        }

        /// <summary>
        /// Gets the currently opened world.
        /// </summary>
        public World? World => _world;

        /// <summary>
        /// Tries to open a world, closing the current <see cref="World"/> if its opened.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="w">The world name to open. The <see cref="IRootedWorldName.Root"/> must be valid and it should be one of the object managed by the store.</param>
        /// <param name="worldStore">The world store.</param>
        /// <returns>True on success, false on error.</returns>
        [MemberNotNullWhen(true,nameof(World))]
        public bool OpenWorld( IActivityMonitor monitor,
                               IRootedWorldName w,
                               IWorldStore worldStore )
        {
            Throw.CheckArgument( !w.Root.IsEmptyPath );
            if( _world != null ) Close( monitor );
            bool resetSecrets = false;
            var keySnapshot = _appContext.KeyStore.CreateSnapshot();
            using( monitor.OpenInfo( $"Opening '{w.FullName}'." ) )
            {
                try
                {
                    var baseProvider = new SimpleServiceContainer();
                    _fs = new FileSystem( w.Root, _appContext.CommandRegistry, _appContext.KeyStore, baseProvider );
                    baseProvider.Add( _fs );
                    baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
                    baseProvider.Add( _appContext.CommandRegistry );
                    baseProvider.Add<IWorldName>( w );
                    baseProvider.Add( w );
                    baseProvider.Add( worldStore );
                    baseProvider.Add( _appContext.KeyStore );
                    baseProvider.Add( _appContext.DefaultReleaseVersionSelector );

                    var original = worldStore.ReadWorldDescription( monitor, w ).Root!;
                    var expanded = XTypedFactory.PreProcess( monitor, original );
                    if( expanded.Errors.Count == 0 )
                    {
                        Debug.Assert( expanded.Result != null );
                        _root = _xFactory.CreateInstance<XTypedObject>( monitor, expanded.Result, baseProvider );
                        if( _root != null )
                        {
                            var xW = _root.Descendants<IXTypedObjectProvider<World>>().FirstOrDefault();
                            if( xW != null )
                            {
                                // We try to load the world even if required secret are not present.
                                _world = xW.GetObject( monitor );
                                if( _world == null )
                                {
                                    // The world couldn't be opened. Is it because of missing secrets?
                                    var missingSecrets = _appContext.KeyStore.Infos.Where( secret => secret.IsRequired && !secret.IsSecretAvailable ).Select( s => s.Name );
                                    if( missingSecrets.Any() )
                                    {
                                        monitor.Error( $"Missing one or more secrets. These are required to open the world '{w.FullName}': {missingSecrets.Concatenate()}." );
                                        resetSecrets = true;
                                    }
                                }
                            }
                            else monitor.Error( "Missing expected World definition element." );
                        }
                    }
                    if( World != null )
                    {
                        return true;
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( ex );
                }
            }
            if( resetSecrets ) keySnapshot.RestoreTo( _appContext.KeyStore );
            DoClose( monitor, null );
            return false;
        }

        /// <summary>
        /// Closes the world and dispose the disposable <see cref="XTypedObject"/> and file system.
        /// Exceptions are thrown.
        /// </summary>
        public void Dispose()
        {
            if( _fs != null )
            {
                if( _root != null )
                {
                    foreach( var e in _root.Descendants<IDisposable>().Reverse() )
                    {
                        e.Dispose();
                    }
                }
                _fs.Dispose();
                _fs = null;
                _root = null;
                _world = null;
            }
        }

        /// <summary>
        /// Closes the <see cref="World"/> if it's opened.
        /// This acts as a protected <see cref="Dispose"/>: exceptions are caught and logged. 
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        public void Close( IActivityMonitor monitor )
        {
            if( _world != null ) DoClose( monitor, $"Closing {_world.WorldName}." );
        }

        void DoClose( IActivityMonitor monitor, string? info )
        {
            if( _fs != null )
            {
                using( info != null ? monitor.OpenInfo( info ) : null )
                {
                    try
                    {
                        Dispose();
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( ex );
                    }
                }
                _root = null;
                _fs = null;
                _world = null;
            }
        }

    }
}
