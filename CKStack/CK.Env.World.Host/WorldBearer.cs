using CK.Core;
using CK.SimpleKeyVault;

using System;
using System.Diagnostics;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Bears a world and manages its opening and closing.
    /// </summary>
    public class WorldBearer
    {
        FileSystem? _fs;
        XTypedObject? _root;
        World? _world;

        /// <summary>
        /// Gets the currently opened world.
        /// </summary>
        public World? World => _world;

        /// <summary>
        /// Tries to open a world, closing the current <see cref="World"/> if its opened.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="w">The world name to open. The <see cref="IRootedWorldName.Root"/> must be valid and it should be one of the object managed by the store.</param>
        /// <param name="factory"></param>
        /// <param name="command"></param>
        /// <param name="worldStore"></param>
        /// <param name="keyStore"></param>
        /// <param name="releaseVersionSelectorFactory"></param>
        /// <returns>True on success, false on error.</returns>
        public bool OpenWorld( IActivityMonitor monitor,
                               IRootedWorldName w,
                               XTypedFactory factory,
                               CommandRegister command,
                               WorldStore worldStore,
                               SecretKeyStore keyStore,
                               Func<IReleaseVersionSelector> releaseVersionSelectorFactory )
        {
            if( w.Root.IsEmptyPath ) throw new ArgumentException( nameof( NormalizedPath.IsEmptyPath ) );
            if( _world != null ) Close( monitor );
            bool resetSecrets = false;
            var keySnapshot = keyStore.CreateSnapshot();
            using( monitor.OpenInfo( $"Opening '{w.FullName}'." ) )
            {
                try
                {
                    var baseProvider = new SimpleServiceContainer();
                    _fs = new FileSystem( w.Root, command, keyStore, baseProvider );
                    baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
                    baseProvider.Add( command );
                    baseProvider.Add( _fs );
                    baseProvider.Add<IWorldName>( w );
                    baseProvider.Add( w );
                    baseProvider.Add( worldStore );
                    baseProvider.Add( keyStore );
                    baseProvider.Add( releaseVersionSelectorFactory );
                    var original = worldStore.ReadWorldDescription( monitor, w ).Root;
                    var expanded = XTypedFactory.PreProcess( monitor, original );
                    if( expanded.Errors.Count == 0 )
                    {
                        Debug.Assert( expanded.Result != null );
                        _root = factory.CreateInstance<XTypedObject>( monitor, expanded.Result, baseProvider );
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
                                    var missingSecrets = keyStore.Infos.Where( secret => secret.IsRequired && !secret.IsSecretAvailable ).Select( s => s.Name );
                                    if( missingSecrets.Any() )
                                    {
                                        monitor.Error( $"Missing one or more secrets. These are required to open the world '{w.FullName}': {missingSecrets.Concatenate()}." );
                                        resetSecrets = false;
                                    }
                                }
                            }
                            else monitor.Error( "Missing expected World definition element." );
                        }
                    }
                    if( _world != null )
                    {
                        return true;
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( ex );
                }
            }
            if( resetSecrets ) keySnapshot.RestoreTo( keyStore );
            DoClose( monitor, null );
            return false;
        }

        /// <summary>
        /// Closes the <see cref="World"/> if it's opened.
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
                        if( _root != null )
                        {
                            foreach( var e in _root.Descendants<IDisposable>().Reverse() )
                            {
                                e.Dispose();
                            }
                        }
                        _fs.Dispose();
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
