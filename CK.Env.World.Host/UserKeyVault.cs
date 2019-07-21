using CK.Core;
using CK.Env;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CK.Env
{
    public sealed class UserKeyVault : ICommandMethodsProvider, IDisposable
    {
        static readonly NormalizedPath _commandProviderName = "KeyVault";

        readonly SecretKeyStore _store;
        readonly Dictionary<string,string> _vaultContent;
        string _passPhrase;

        public UserKeyVault( NormalizedPath userHostPath, CommandRegister commandRegister )
        {
            _store = new SecretKeyStore();
            _vaultContent = new Dictionary<string, string>();
            KeyVaultKeyName = "CKLI-" + Environment.UserDomainName
                                         .Replace( '-', '_' )
                                         .Replace( '/', '_' )
                                         .Replace( '\\', '_' )
                                         .Replace( '.', '_' )
                                         .ToUpperInvariant();
            KeyVaultPath = userHostPath.AppendPart( KeyVaultKeyName + ".KeyVault.txt" );
            commandRegister.Register( this );
        }

        /// <summary>
        /// Gets the actual key store.
        /// </summary>
        public SecretKeyStore KeyStore => _store;
        
        /// <summary>
        /// Gets the key vault file path.
        /// </summary>
        public string KeyVaultPath { get; }

        /// <summary>
        /// Gets the name of the primary secret required to open the key vault.
        /// This is: "CKLI_" + <see cref="Environment.UserDomainName"/> in upper
        /// case where '\', '/', '.' and '-' are replaced with '_'.
        /// </summary>
        public string KeyVaultKeyName { get; }

        /// <summary>
        /// Gets whether there is a file for this vault. When no file exists it must be created.
        /// </summary>
        public bool KeyVaultFileExists => File.Exists( KeyVaultPath );

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
                    m.OpenInfo( $"Opening existing Key Vault with keys: {keys.Keys.Concatenate()}." );
                    _store.ImportSecretKeys( m, keys );
                    _passPhrase = passPhrase;
                    _vaultContent.Clear();
                    _vaultContent.AddRange( keys );
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
                m.OpenInfo( $"New Key Vault opened." );
            }
            if( _store.Infos.Any( s => !s.IsSecretAvailable ) )
            {
                using( m.OpenWarn( $"Missing secrets:" ) )
                {
                    foreach( var s in _store.Infos.Where( s => !s.IsSecretAvailable ) )
                    {
                        m.Warn( s.ToString() );
                    }
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
            DoSaveKeyVault( m );
            return true;
        }

        void DoSaveKeyVault( IActivityMonitor m )
        {
            foreach( var e in _store.OptimalAvailableInfos )
            {
                _vaultContent[e.Name] = e.Secret;
            }
            m?.Info( $"Saved Key Vault with keys: {_vaultContent.Keys.Concatenate()}." );
            File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _vaultContent, _passPhrase ) );
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
                _store.Clear();
                _vaultContent.Clear();
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


        void IDisposable.Dispose()
        {
            if( IsKeyVaultOpened )
            {
                DoSaveKeyVault( null );
                _passPhrase = null;
            }
        }
    }
}
