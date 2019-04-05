using CK.Core;
using CK.Env;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli
{
    public class CKEnvKeyVault : ISecretKeyStore, ICommandMethodsProvider, IDisposable
    {
        static readonly NormalizedPath _commandProviderName = new NormalizedPath("KeyVault");

        readonly FileSystem _fs;
        readonly Dictionary<string, string> _keys;
        readonly IWorldName _worldName;
        string _passPhrase;

        public CKEnvKeyVault(
            IWorldName worldName,
            FileSystem fs,
            CommandRegister commandRegister)
        {
            _keys = new Dictionary<string, string>();
            _fs = fs;
            _worldName = worldName;
            KeyVaultPath = _fs.Root.AppendPart( $"CKEnv-{_worldName.Name}-KeyVault.txt" );
            KeyVaultKeyName = $"CKENV_{_worldName.Name.ToUpperInvariant().Replace( '-', '_' )}_KEY_VAULT_SECRET_KEY";
            commandRegister.Register( this );
        }

        string KeyVaultPath { get; }

        string KeyVaultKeyName { get; }

        bool KeyVaultFileExists => File.Exists( KeyVaultPath );

        /// <summary>
        /// Gets whether the key vault bound to the current <see cref="IWorldName"/> is opened.
        /// </summary>
        public bool IsKeyVaultOpened => _passPhrase != null;

        NormalizedPath ICommandMethodsProvider.CommandProviderName => _commandProviderName;

        /// <summary>
        /// Gets whether the key vault is closed.
        /// </summary>
        public bool CanOpenKeyVault => !IsKeyVaultOpened;

        /// <summary>
        /// Opens the key vault bound to the current <see cref="IWorldName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="passPhrase">The key vault pass phrase.</param>
        /// <returns>True on success.</returns>
        [CommandMethod(confirmationRequired:false)]
        public bool OpenKeyVault( IActivityMonitor m, string passPhrase )
        {
            if( !CheckPassPhraseValidity( m, passPhrase ) ) return false;
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

        /// <summary>
        /// Gets whether the key vault is opened.
        /// </summary>
        public bool CanSaveKeyVault => IsKeyVaultOpened;

        /// <summary>
        /// Saves the current secrets to the previously opened key vault bound to the current <see cref="IWorldName"/>
        /// with a new passphrase or uses the existing one.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="newPassPhrase">Optional new passphrase.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod( confirmationRequired: false )]
        public bool SaveKeyVault( IActivityMonitor m, string newPassPhrase = null )
        {
            if( _passPhrase == null )
            {
                m.Info( "Key vault is closed." );
                return false;
            }
            if( newPassPhrase == null )
            {
                newPassPhrase = _passPhrase;
            }
            else
            {
                if( !CheckPassPhraseValidity( m, newPassPhrase ) ) return false;
                _passPhrase = newPassPhrase;
            }
            m.Info( $"Saved Key Vault with keys: {_keys.Keys.Concatenate()}." );
            File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _keys, _passPhrase ) );
            return true;
        }

        /// <summary>
        /// Clears the current scret that may exist in memory and the persisted key vault bound
        /// to the current <see cref="IWorldName"/>
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        [CommandMethod]
        public void ClearKeyVault( IActivityMonitor m )
        {
            if( IsKeyVaultOpened )
            {
                _keys.Clear();
                _passPhrase = null;
                if( File.Exists( KeyVaultPath ) )
                {
                    try
                    {
                        File.Delete( KeyVaultPath );
                    }
                    catch( Exception ex )
                    {
                        m.Error( $"Unable to delete file '{KeyVaultPath}'.", ex );
                    }
                }
            }
        }

        /// <summary>
        /// May be modified to enforce constraints on the passphrase.
        /// </summary>
        static bool CheckPassPhraseValidity( IActivityMonitor m, string passPhrase )
        {
            if( String.IsNullOrEmpty( passPhrase ) )
            {
                m.Warn( "Invalid pass phrase." );
                return false;
            }
            return true;
        }

        /// <summary>
        /// Implements <see cref="ISecretKeyStore.GetSecretKey(IActivityMonitor, string, bool, string)"/> by
        /// first using the key vault bound to the current <see cref="IWorldName"/> if it exists, asking for its
        /// passphrase if it needs to be opened.
        /// When the key is not found, the user must provide it and the secret is stored. 
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret nam.</param>
        /// <param name="throwOnEmpty">True to throw an exception if the secret cannot be obtained.</param>
        /// <param name="message">Optional message displayed if the secret need to be entered.</param>
        /// <returns>The secret or null if it cannot be obtained.</returns>
        /// <returns></returns>
        public string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty, string message = null )
        {
            if( !IsKeyVaultOpened )
            {
                var passPhrase = Environment.GetEnvironmentVariable( KeyVaultKeyName );
                if( passPhrase != null )
                {
                    m.Info( $"Using {KeyVaultKeyName} environment variable to open the Key Vault for {_worldName.FullName}." );
                }
                else
                {
                    passPhrase = PromptValue( KeyVaultKeyName, $"Open the Key Vault for {_worldName.FullName}.", throwOnEmpty: false );
                }

                if( passPhrase != null ) OpenKeyVault( m, passPhrase );
            }
            bool exists = _keys.TryGetValue( name, out var value );
            if( !exists || (value == null && throwOnEmpty) )
            {
                value = PromptValue( name, message, throwOnEmpty );
                _keys.Add( name, value );
                if( IsKeyVaultOpened )
                {
                    File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _keys, _passPhrase ) );
                }
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
                File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _keys, _passPhrase ) );
                _passPhrase = null;
            }
        }
    }
}
