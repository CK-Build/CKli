using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CK.Core;
using CK.Text;

namespace CK.Env
{
    /// <summary>
    /// Exposes a <see cref="CurrentWorld"/> among the ones in a <see cref="Store"/>.
    /// </summary>
    public sealed class WorldSelector : ICommandMethodsProvider
    {
        readonly XTypedFactory _factory;
        readonly SecretKeyStore _userKeyStore;
        readonly CommandRegister _command;
        readonly IBasicApplicationLifetime _appLife;
        readonly HashSet<ICommandHandler> _existingCommands;
        FileSystem _fs;
        XTypedObject _root;

        /// <summary>
        /// Initializes a new WorldSelector that exposes a <see cref="CurrentWorld"/>.
        /// </summary>
        /// <param name="store">The world store.</param>
        /// <param name="commandRegister">The command register.</param>
        /// <param name="factory">The factory for XTypedObjects.</param>
        /// <param name="userKeyStore">The user key store.</param>
        /// <param name="appLife">Simple application lifetime controller.</param>
        public WorldSelector( WorldStore store, CommandRegister commandRegister, XTypedFactory factory, SecretKeyStore userKeyStore, IBasicApplicationLifetime appLife )
        {
            Store = store ?? throw new ArgumentNullException( nameof( store ) );
            _command = commandRegister ?? throw new ArgumentNullException( nameof( commandRegister ) );
            _userKeyStore = userKeyStore ?? throw new ArgumentNullException( nameof( userKeyStore ) );
            _appLife = appLife ?? throw new ArgumentNullException( nameof( appLife ) );
            _factory = factory ?? throw new ArgumentNullException( nameof( factory ) );
            commandRegister.Register( this );
            _existingCommands = new HashSet<ICommandHandler>( commandRegister.GetAllCommands( false ) );
        }

        /// <summary>
        /// Gets the store.
        /// </summary>
        public WorldStore Store { get; }

        /// <summary>
        /// Gets the current world.
        /// Can be null if <see cref="Open(IActivityMonitor, string)"/> failed.
        /// </summary>
        public World CurrentWorld { get; private set; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => UserHost.HomeCommandPath;

        /// <summary>
        /// Closing current world: current world must not be null.
        /// </summary>
        public bool CanClose => CurrentWorld != null;

        /// <summary>
        /// Closes the current world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        [CommandMethod( confirmationRequired: false )]
        public void Close( IActivityMonitor m )
        {
            if( !CanClose ) throw new InvalidOperationException();
            m.Info( $"Closing current world: {CurrentWorld.WorldName.FullName}." );
            Close();
        }

        /// <summary>
        /// Opening a world requires the current world to be closed first.
        /// </summary>
        public bool CanOpen => CurrentWorld == null;

        /// <summary>
        /// Tries to open a world in <see cref="Store"/> by its full name (or its One based index).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="worldFullNameOr1BasedIndex">The name or the index in the store.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Open( IActivityMonitor m, string worldFullNameOr1BasedIndex )
        {
            if( !CanOpen ) throw new InvalidOperationException();
            using( m.OpenInfo( $"Opening world '{worldFullNameOr1BasedIndex}'." ) )
            {
                var all = Store.ReadWorlds( m );
                if( all == null ) return false;
                IRootedWorldName w;
                if( Int32.TryParse( worldFullNameOr1BasedIndex, out var idx ) )
                {
                    if( idx >= 1 && idx <= all.Count )
                    {
                        w = all[idx - 1];
                        m.Info( $"World '{worldFullNameOr1BasedIndex}' is world {w.FullName}." );
                    }
                    else
                    {
                        m.Error( $"Invalid index: {idx} out of {all.Count}." );
                        return false;
                    }
                }
                else
                {
                    w = all.FirstOrDefault( x => x.FullName.Equals( worldFullNameOr1BasedIndex, StringComparison.OrdinalIgnoreCase ) );
                    if( w == null )
                    {
                        m.Error( $"Unable to find World {worldFullNameOr1BasedIndex} among {all.Select( x => x.FullName ).Concatenate()}." );
                        return false;
                    }
                }
                Debug.Assert( w != null );
                Close();
                var keySnapshot = _userKeyStore.CreateSnapshot();
                var baseProvider = new SimpleServiceContainer();
                _fs = new FileSystem( w.Root, _command, _userKeyStore, baseProvider );
                baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
                baseProvider.Add( _command );
                baseProvider.Add( _fs );
                baseProvider.Add<IWorldName>( w );
                baseProvider.Add<WorldStore>( Store );
                baseProvider.Add( _appLife );
                baseProvider.Add( _userKeyStore );
                var original = Store.ReadWorldDescription( m, w ).Root;
                var expanded = XTypedFactory.PreProcess( m, original );
                if( expanded.Errors.Count > 0 ) return false;
                _root = _factory.CreateInstance<XTypedObject>( m, expanded.Result, baseProvider );
                if( _root != null )
                {
                    var xW = _root.Descendants<IXTypedObjectProvider<World>>().FirstOrDefault();
                    if( xW != null )
                    {
                        if( _userKeyStore.Infos.All( secret => !secret.IsRequired || secret.IsSecretAvailable ) )
                        {
                            CurrentWorld = xW.GetObject( m );
                            if( CurrentWorld != null ) return true;
                        }
                        else
                        {
                            var missing = _userKeyStore.Infos.Where( secret => secret.IsRequired && !secret.IsSecretAvailable ).Select( s => s.Name ).Concatenate();
                            m.Error( $"Missing one or more secrets. These are required to continue: {missing}." );
                        }
                    }
                    else m.Error( "Missing expected World definition element." );
                }
                keySnapshot.RestoreTo( _userKeyStore );
                Close();
                return false;
            }
        }

        void Close()
        {
            if( _fs != null )
            {
                if( _root != null )
                {
                    foreach( var e in _root.Descendants<IDisposable>().Reverse() )
                    {
                        e.Dispose();
                    }
                    _root = null;
                }
                _command.UnregisterAll( keepFilter: _existingCommands.Contains );
                if( CurrentWorld != null )
                {
                    CurrentWorld = null;
                }
                _fs.Dispose();
                _fs = null;
            }
        }

        public void Dispose() => Close();
    }
}
