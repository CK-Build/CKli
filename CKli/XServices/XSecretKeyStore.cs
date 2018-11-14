using CK.Core;
using CK.Env;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class XSecretKeyStore : XTypedObject, ISecretKeyStore, CK.NuGetClient.ISecretKeyStore, IDisposable
    {
        readonly FileSystem _fs;
        readonly Dictionary<string, string> _keys;
        readonly IWorldName _worldName;
        string _passPhrase;

        public XSecretKeyStore(
            IWorldName worldName,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            _keys = new Dictionary<string, string>();
            _fs = fs;
            _worldName = worldName;
            initializer.Services.Add( this );
            KeyVaultPath = _fs.Root.AppendPart( $"CKEnv-{_worldName.Name}-KeyVault.txt" );
            KeyVaultKeyName = $"CKENV_{_worldName.Name.ToUpperInvariant().Replace( '-', '_' )}_KEY_VAULT_SECRET_KEY";
        }

        string KeyVaultPath { get; }

        string KeyVaultKeyName { get; }

        bool KeyVaultFileExists => File.Exists( KeyVaultPath );

        bool IsKeyVaultOpened => _passPhrase != null;

        public bool OpenKeyVault( IActivityMonitor m, string passPhrase )
        {
            if( !CheckPassPhrase( m, passPhrase ) ) return false;
            if( _passPhrase != null )
            {
                m.Info( $"Key Vault is already opened." );
                return true;
            }
            if( KeyVaultFileExists )
            {
                try
                {
                    var keys = KeyVault.DecryptValues( File.ReadAllText( KeyVaultPath ), passPhrase );
                    _passPhrase = passPhrase;
                    _keys.Clear();
                    _keys.AddRange( keys );
                    m.Info( $"Opened existing Key Vault with keys: {_keys.Keys.Concatenate()}." );
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
            }
            else
            {
                _passPhrase = passPhrase;
                m.Info( $"Opened new Key Vault." );
            }
            return true;
        }

        public bool SaveKeyVault( IActivityMonitor m, string newPassPhrase = null )
        {
            if( _passPhrase == null )
            {
                m.Info( "Key vault is closed." );
                return false;
            }
            if( newPassPhrase == null ) newPassPhrase = _passPhrase;
            else
            {
                if( !CheckPassPhrase( m, newPassPhrase ) ) return false;
                _passPhrase = newPassPhrase;
            }
            m.Info( $"Saved Key Vault with keys: {_keys.Keys.Concatenate()}." );
            File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _keys, _passPhrase ) );
            return true;
        }

        static bool CheckPassPhrase( IActivityMonitor m, string passPhrase )
        {
            if( String.IsNullOrEmpty( passPhrase ) ) throw new ArgumentNullException( nameof( passPhrase ) );
            return true;
        }

        public string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty, string message = null )
        {
            if( KeyVaultFileExists && !IsKeyVaultOpened )
            {
                var passPhrase = PromptValue( KeyVaultKeyName, $"Open the CKEnv Key Vault for {_worldName.FullName}.", throwOnEmpty: false );
                if( passPhrase != null ) OpenKeyVault( m, passPhrase );
            }
            bool exists = _keys.TryGetValue( name, out var value );
            if( !exists || (value == null && throwOnEmpty) )
            {
                value = PromptValue( name, message, throwOnEmpty );
                _keys.Add( name, value );
            }
            return value;
        }

        static string PromptValue( string name, string message, bool throwOnEmpty )
        {
            string value;
            if( message != null ) Console.WriteLine( message );
            if( throwOnEmpty ) Console.WriteLine( "!Required!" );
            Console.Write( $"Enter {name}: " );
            var v = Console.ReadLine();
            value = String.IsNullOrEmpty( v ) ? null : v;
            if( value == null && throwOnEmpty )
            {
                throw new Exception( $"Secret '{name}' is required. {message}" );
            }
            return value;
        }

        void IDisposable.Dispose()
        {
            if( IsKeyVaultOpened )
            {
                _passPhrase = null;
                File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _keys, _passPhrase ) );
            }
        }
    }
}
