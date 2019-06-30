using CK.Core;
using CK.Env;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CKli
{
    public class CKEnvKeyVault : ISecretKeyStore, ICommandMethodsProvider, IDisposable
    {
        static readonly NormalizedPath _commandProviderName = new NormalizedPath("KeyVault");

        readonly Dictionary<string, string> _keys;
        readonly Dictionary<string, string> _secrets;
        readonly IWorldName _worldName;
        string _passPhrase;

        public CKEnvKeyVault(
            IWorldName worldName,
            NormalizedPath localWorldPath,
            CommandRegister commandRegister)
        {
            _keys = new Dictionary<string, string>();
            _secrets = new Dictionary<string, string>();
            _worldName = worldName;
            KeyVaultPath = localWorldPath.AppendPart( $"CKEnv-{_worldName.Name}-KeyVault.txt" );
            KeyVaultKeyName = $"CKENV_{_worldName.Name.ToUpperInvariant().Replace( '-', '_' )}_KEY_VAULT_SECRET_KEY";
            commandRegister.Register( this );
        }

        /// <summary>
        /// Gets the key vault file path.
        /// </summary>
        string KeyVaultPath { get; }

        /// <summary>
        /// Gets the name of the primary secret required to open the key vault.
        /// This is built from the <see cref="IWorldName.Name"/> (CKENV_XXX_KEY_VAULT_SECRET_KEY).
        /// </summary>
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
            if( !CheckPassPhraseConstraints( m, passPhrase ) ) return false;
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
                    _secrets.Clear();
                    _secrets.AddRange( keys );
                    m.OpenInfo( $"Opened existing Key Vault with keys: {_secrets.Keys.Concatenate()}." );
                }
                catch( Exception ex )
                {
                    m.Error( "Unable to open the key vault.", ex );
                    return false;
                }
            }
            else
            {
                _passPhrase = passPhrase;
                m.OpenInfo( $"Opened new Key Vault." );
            }
            var missing = _keys.Keys.Except( _secrets.Keys );
            if( missing.Any() )
            {
                foreach( var km in missing )
                {
                    m.Warn( $"Missing secret '{km}': {_keys[km]}" );
                }
            }
            m.CloseGroup();
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
                if( !CheckPassPhraseConstraints( m, newPassPhrase ) ) return false;
                _passPhrase = newPassPhrase;
            }
            m.Info( $"Saved Key Vault with keys: {_secrets.Keys.Concatenate()}." );
            File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _secrets, _passPhrase ) );
            return true;
        }

        /// <summary>
        /// Clears the current secrets that may exist in memory and the persisted key vault bound
        /// to the current <see cref="IWorldName"/> and closes the vault.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        [CommandMethod]
        public void DeleteKeyVault( IActivityMonitor m )
        {
            if( IsKeyVaultOpened )
            {
                _secrets.Clear();
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
        /// Currently only requires it to be not null nor empty.
        /// </summary>
        static bool CheckPassPhraseConstraints( IActivityMonitor m, string passPhrase )
        {
            if( String.IsNullOrEmpty( passPhrase ) )
            {
                m.Warn( "Invalid pass phrase." );
                return false;
            }
            return true;
        }


        /// <summary>
        /// Declares a secret key.
        /// Can be called as many times as needed: the <paramref name="descriptionBuilder"/> can
        /// compose the final description.
        /// </summary>
        /// <param name="secretName">The name to declare. Must not be empty.</param>
        /// <param name="descriptionBuilder">
        /// The description builder that accepts the current description (initially null) and must return the combined one.
        /// Must not be null.
        /// </param>
        public void DeclareSecretKey( string secretName, Func<string, string> descriptionBuilder )
        {
            if( String.IsNullOrWhiteSpace( secretName ) ) throw new ArgumentException( nameof( secretName ) );
            if( descriptionBuilder == null ) throw new ArgumentNullException( nameof( descriptionBuilder ) );
            _keys.TryGetValue( secretName, out var desc );
            _keys[secretName] = descriptionBuilder( desc );
        }

        /// <summary>
        /// Implements <see cref="ISecretKeyStore.IsSecretKeyAvailable(string)"/>.
        /// </summary>
        /// <param name="secretName">The secret name.</param>
        /// <returns>Null if the secret has not been declared, false if it has been declared but not known.</returns>
        public bool? IsSecretKeyAvailable( string secretName )
        {
            if( String.IsNullOrWhiteSpace( secretName ) ) throw new ArgumentException( nameof( secretName ) );
            if( !_keys.ContainsKey( secretName ) ) return null;
            return _secrets.ContainsKey( secretName );
        }

        /// <summary>
        /// Implements <see cref="ISecretKeyStore.GetSecretKey(IActivityMonitor, string, bool)"/>. 
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret name.</param>
        /// <param name="throwOnEmpty">True to throw an <see cref="Exception"/> if the secret cannot be obtained.</param>
        /// <returns>The secret or null if it cannot be obtained.</returns>
        /// <returns></returns>
        public string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty )
        {
            string pass = null;
            bool exists = IsKeyVaultOpened && _secrets.TryGetValue( name, out pass );
            if( !exists )
            {
                if( !_keys.TryGetValue( name, out var desc ) )
                {
                    throw new InvalidOperationException( $"Secret '{name}' is not declared. It cannot be required." );
                }
                if( throwOnEmpty )
                {
                    throw new Exception( $"Secret '{name}' is required. Description: {desc}" );
                }
            }
            return pass;
        }

        void IDisposable.Dispose()
        {
            if( IsKeyVaultOpened )
            {
                File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _secrets, _passPhrase ) );
                _passPhrase = null;
            }
        }
    }
}
