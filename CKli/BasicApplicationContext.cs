using CK.Core;
using CK.Env;
using CK.SimpleKeyVault;
using System;

namespace CKli
{
    sealed class BasicApplicationContext : ICkliApplicationContext
    {
        public static ICkliApplicationContext? Create( IActivityMonitor monitor )
        {
            NormalizedPath userHostPath = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
            userHostPath = userHostPath.AppendPart( "CKli" );
            return Create( monitor, userHostPath );
        }

        public static ICkliApplicationContext? Create( IActivityMonitor monitor, NormalizedPath userHostPath )
        {
            var keyVault = new FileKeyVault( userHostPath.AppendPart( "Personal.KeyVault.txt" ) );
            if( !keyVault.OpenKeyVault( monitor ) ) return null;
            return Create( userHostPath, keyVault );
        }

        public static ICkliApplicationContext Create( NormalizedPath userHostPath, FileKeyVault keyVault )
        {
            return new BasicApplicationContext( userHostPath, keyVault.KeyStore, new CommandRegistry(), new CoreTypes( typeof(XCKliWorld) ), new ConsoleReleaseVersionSelector() );
        }

        BasicApplicationContext( NormalizedPath userHostPath,
                                 SecretKeyStore keyStore,
                                 CommandRegistry commandRegistry,
                                 IXTypedMap xTypedMap,
                                 IReleaseVersionSelector defaultSelector )
        {
            UserHostPath = userHostPath;
            KeyStore = keyStore;
            CommandRegistry = commandRegistry;
            CoreXTypedMap = xTypedMap;
            DefaultReleaseVersionSelector = defaultSelector;
        }

        public NormalizedPath UserHostPath { get; }

        public SecretKeyStore KeyStore { get; }

        public CommandRegistry CommandRegistry { get; }

        public IXTypedMap CoreXTypedMap { get; }

        public IReleaseVersionSelector DefaultReleaseVersionSelector { get; }
    }
}
