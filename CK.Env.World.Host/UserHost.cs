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
        readonly CommandRegister _commandRegister;
        readonly UserKeyVault _keyVault;
        readonly SimpleWorldLocalMapping _worldMapping;
        readonly GitWorldStore _store;
        readonly WorldSelector _selector;

        public UserHost( IBasicApplicationLifetime lifetime )
        {
            NormalizedPath userHostPath = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
            userHostPath = userHostPath.AppendPart( "CKli" );

            _xTypedObjectfactory = new XTypedFactory();
            _commandRegister = new CommandRegister();
            _keyVault = new UserKeyVault( userHostPath, _commandRegister );
            _worldMapping = new SimpleWorldLocalMapping( userHostPath.AppendPart( "WorldLocalMapping.txt" ) );
            _store = new GitWorldStore( userHostPath, _worldMapping, _keyVault.KeyStore, _commandRegister );
            _selector = new WorldSelector( _store, _commandRegister, _xTypedObjectfactory, _keyVault.KeyStore, lifetime );
        }

    }
}
