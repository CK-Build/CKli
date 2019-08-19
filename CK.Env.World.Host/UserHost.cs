using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public sealed class UserHost : ICommandMethodsProvider
    {
        public static readonly NormalizedPath HomeCommandPath = "Home";

        readonly XTypedFactory _xTypedObjectfactory;
        readonly SimpleWorldLocalMapping _worldMapping;
        readonly GitWorldStore _store;

        public UserHost( IBasicApplicationLifetime lifetime, NormalizedPath userHostPath )
        {
            ApplicationLifetime = lifetime;
            CommandRegister = new CommandRegister();
            _xTypedObjectfactory = new XTypedFactory();
            UserKeyVault = new UserKeyVault( userHostPath );
            _worldMapping = new SimpleWorldLocalMapping( userHostPath.AppendPart( "WorldLocalMapping.txt" ) );
            _store = new GitWorldStore( userHostPath, _worldMapping, UserKeyVault.KeyStore, CommandRegister );
            WorldSelector = new WorldSelector( _store, CommandRegister, _xTypedObjectfactory, UserKeyVault.KeyStore, lifetime );
            CommandRegister.Register( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => UserHost.HomeCommandPath;

        public CommandRegister CommandRegister { get; }

        public IBasicApplicationLifetime ApplicationLifetime { get; }

        public GitWorldStore WorldStore => _store;

        public WorldSelector WorldSelector { get; }

        public UserKeyVault UserKeyVault { get; }

        public void Initialize( IActivityMonitor m )
        {
            if( !UserKeyVault.IsKeyVaultOpened )
            {
                // First initialization: the key vault is not opened.
                _xTypedObjectfactory.AutoRegisterFromLoadedAssemblies( m );
                _store.ReadStacksFromFile( m );
            }
            else
            {
                _store.PullAll( m );
            }
        }

        [CommandMethod]
        public void Refresh( IActivityMonitor m )
        {
            _store.PullAll( m );
            if( WorldSelector.CanClose )
            {
                var opened = WorldSelector.CurrentWorld.WorldName.FullName;
                if( opened != null )
                {
                    WorldSelector.Close( m );
                    WorldSelector.Open( m, opened );
                }
            }
        }
    }
}
