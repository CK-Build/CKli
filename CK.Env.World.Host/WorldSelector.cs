using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Exposes a <see cref="CurrentWorld"/> among the ones in a <see cref="Store"/>.
    /// </summary>
    public sealed partial class WorldSelector : ICommandMethodsProvider
    {
        readonly XTypedFactory _factory;
        readonly SecretKeyStore _userKeyStore;
        readonly CommandRegister _command;
        readonly HashSet<ICommandHandler> _otherCommands;
        readonly Func<IReleaseVersionSelector> _releaseVersionSelectorFactory;
        readonly WorldBearer _worldBearer;

        /// <summary>
        /// Initializes a new WorldSelector that exposes a <see cref="CurrentWorld"/>.
        /// </summary>
        /// <param name="store">The world store.</param>
        /// <param name="commandRegister">The command register.</param>
        /// <param name="factory">The factory for XTypedObjects.</param>
        /// <param name="userKeyStore">The user key store.</param>
        /// <param name="releaseVersionSelectorFactory">Factory for <see cref="IReleaseVersionSelector"/> that the world will use.</param>
        /// <param name="appLife">Simple application lifetime controller.</param>
        public WorldSelector(
            GitWorldStore store,
            CommandRegister commandRegister,
            XTypedFactory factory,
            SecretKeyStore userKeyStore,
            Func<IReleaseVersionSelector> releaseVersionSelectorFactory )
        {
            Store = store ?? throw new ArgumentNullException( nameof( store ) );
            _command = commandRegister ?? throw new ArgumentNullException( nameof( commandRegister ) );
            _userKeyStore = userKeyStore ?? throw new ArgumentNullException( nameof( userKeyStore ) );
            _factory = factory ?? throw new ArgumentNullException( nameof( factory ) );
            commandRegister.Register( this );
            _otherCommands = new HashSet<ICommandHandler>( commandRegister.GetAllCommands( false ) );
            _releaseVersionSelectorFactory = releaseVersionSelectorFactory;
            _worldBearer = new WorldBearer();
        }

        /// <summary>
        /// Gets the store.
        /// </summary>
        public GitWorldStore Store { get; }

        /// <summary>
        /// Gets the current world.
        /// Can be null if <see cref="Open(IActivityMonitor, string)"/> failed.
        /// </summary>
        public World? CurrentWorld => _worldBearer.World;

        NormalizedPath ICommandMethodsProvider.CommandProviderName => MultipleWorldHome.WorldCommandPath;

        /// <summary>
        /// Closing current world: current world must not be null.
        /// </summary>
        public bool CanCloseWorld => _worldBearer.World != null;

        /// <summary>
        /// Closes the current world.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        [CommandMethod( confirmationRequired: false )]
        public void CloseWorld( IActivityMonitor monitor )
        {
            if( !CanCloseWorld ) throw new InvalidOperationException();
            Debug.Assert( CurrentWorld != null );
            _worldBearer.Close( monitor );
            _command.UnregisterAll( _otherCommands.Contains );
            Store.DisableRepositoryAndStacksCommands = false;
        }

        /// <summary>
        /// Opening a world requires the current world to be closed first.
        /// </summary>
        public bool CanOpenWorld => CurrentWorld == null;

        /// <summary>
        /// Tries to open a world in <see cref="Store"/> by its full name (or its One based index).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="worldFullNameOr1BasedIndex">The name or the index in the store.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod( confirmationRequired: false )]
        public bool OpenWorld( IActivityMonitor m, string worldFullNameOr1BasedIndex )
        {
            if( !CanOpenWorld ) throw new InvalidOperationException();
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
                if( w.Root.IsEmptyPath )
                {
                    m.Error( $"World '{w.FullName}' is not mapped. Use 'World/{nameof( Store.SetWorldMapping )}' command." );
                    return false;
                }
                Debug.Assert( w != null );

                bool success = _worldBearer.OpenWorld( m, w, _factory, _command, Store, _userKeyStore, _releaseVersionSelectorFactory );
                if( success )
                {
                    Store.DisableRepositoryAndStacksCommands = true;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

    }
}
