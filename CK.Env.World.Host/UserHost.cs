using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public sealed class UserHost
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
            UserKeyVault = new UserKeyVault( userHostPath, CommandRegister );
            _worldMapping = new SimpleWorldLocalMapping( userHostPath.AppendPart( "WorldLocalMapping.txt" ) );
            _store = new GitWorldStore( userHostPath, _worldMapping, UserKeyVault.KeyStore, CommandRegister );
            WorldSelector = new WorldSelector( _store, CommandRegister, _xTypedObjectfactory, UserKeyVault.KeyStore, lifetime );
        }

        public CommandRegister CommandRegister { get; }

        public IBasicApplicationLifetime ApplicationLifetime { get; }

        public WorldStore WorldStore => _store;

        public WorldSelector WorldSelector { get; }

        public UserKeyVault UserKeyVault { get; }


        public void Initialize( IActivityMonitor m )
        {
            _xTypedObjectfactory.AutoRegisterFromLoadedAssemblies( m );
            _store.ReadStacksFromFile( m );
        }
    }
}
