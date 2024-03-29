using CK.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CK.SimpleKeyVault
{
    /// <summary>
    /// Simple file based key vault that handles a <see cref="KeyStore"/>: keys are automatically
    /// loaded and saved in this file.
    /// <para>
    /// This is thread-safe (protected by a basic sync lock) and interactions with the <see cref="KeyStore"/>
    /// (that is also thread-safe) are done outside of any lock.
    /// </para>
    /// </summary>
    public sealed class FileKeyVault : IDisposable
    {
        readonly SecretKeyStore _store;
        // This is used as the lock.
        readonly Dictionary<string, string?> _vaultContent;
        string? _passPhrase;

        /// <summary>
        /// Initializes a new vault based on a file path (that may exist or not).
        /// </summary>
        /// <param name="keyVaultPath">The vault file path.</param>
        public FileKeyVault( in NormalizedPath keyVaultPath )
        {
            _store = new SecretKeyStore();
            _store.SecretDeclared += OnSecretDeclared;
            _vaultContent = new Dictionary<string, string?>();
            KeyVaultPath = keyVaultPath;
        }

        internal IDisposable AcquireLock()
        {
            System.Threading.Monitor.Enter( _vaultContent );
            return Util.CreateDisposableAction( () => System.Threading.Monitor.Exit( _vaultContent ) );
        }

        void OnSecretDeclared( object? sender, SecretKeyInfoDeclaredArgs e )
        {
            var key = e.Declared;
            if( key.IsSecretAvailable )
            {
                // Secret has been set by other means (typically the "undeclared").
                // We don't update our content, since the "save" refreshes its content from the "Optimal keys".
            }
            else
            {
                string? secret = null;
                if( IsKeyVaultOpened )
                {
                    using( AcquireLock() )
                    {
                        if( IsKeyVaultOpened )
                        {
                            _vaultContent.TryGetValue( e.Declared.Name, out secret );
                        }
                    }
                    if( secret != null )
                    {
                        key.SetSecret( secret );
                    }
                }
            }
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
        /// Gets whether there is a file for this vault. When no file exists it must be created.
        /// </summary>
        public bool KeyVaultFileExists => File.Exists( KeyVaultPath );

        /// <summary>
        /// Gets whether this key vault is opened.
        /// </summary>
        public bool IsKeyVaultOpened => _passPhrase != null;

        /// <summary>
        /// Updates or clears the secret of a declared key in the <see cref="KeyStore"/>.
        /// Returns true if secret has been changed, false otherwise.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="key">The secret name to update.</param>
        /// <param name="secret">The secret to update. Null or empty clears the secret.</param>
        /// <param name="autoSave">False to not automatically saves the vault.</param>
        /// <returns>True if secret has been changed, false otherwise.</returns>
        public bool UpdateSecret( IActivityMonitor m, string key, string? secret, bool autoSave = true )
        {
            if( !_store.SetSecret( m, key, secret ) ) return false;
            using( AcquireLock() )
            {
                if( autoSave && IsKeyVaultOpened )
                {
                    if( string.IsNullOrEmpty( secret ) )
                    {
                        _vaultContent.Remove( key );
                    }
                    DoSaveKeyVault( m );
                }
            }
            return true;
        }

        /// <summary>
        /// Gets whether the key vault is closed.
        /// </summary>
        public bool CanOpenKeyVault => !IsKeyVaultOpened;

        /// <summary>
        /// Opens the key vault.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="passPhrase">The key vault pass phrase.</param>
        /// <returns>True on success.</returns>
        public bool OpenKeyVault( IActivityMonitor m, string passPhrase = "CKli" )
        {
            using var l = AcquireLock();

            if( !CheckPassPhraseConstraints( m, passPhrase ) )
            {
                return false;
            }
            if( _passPhrase != null )
            {
                m.Info( $"Key Vault is already opened." );
                return true;
            }
            if( KeyVaultFileExists )
            {
                try
                {
                    Dictionary<string, string?>? keys;
                    try
                    {
                        keys = KeyVault.DecryptValues( File.ReadAllText( KeyVaultPath ), passPhrase );
                    }
                    catch
                    {
                        if( passPhrase == "CKli" ) return false;
                        throw;
                    }
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
        /// Saves the current secrets to the key vault with a new passphrase
        /// or uses the existing one.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="newPassPhrase">Optional new passphrase.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SaveKeyVault( IActivityMonitor m, string? newPassPhrase = null )
        {
            using var l = AcquireLock();

            if( _passPhrase == null )
            {
                m.Info( "Key vault is closed." );
                return false;
            }
            if( newPassPhrase != null )
            {
                if( !CheckPassPhraseConstraints( m, newPassPhrase ) ) return false;
                _passPhrase = newPassPhrase;
            }
            DoSaveKeyVault( m );
            return true;
        }

        void DoSaveKeyVault( IActivityMonitor? m )
        {
            Debug.Assert( _passPhrase != null, "The file is opened." );
            foreach( var e in _store.OptimalNamedSecrets )
            {
                _vaultContent[e.Name] = e.Secret;
            }
            m?.Info( $"Saved Key Vault with keys: {_vaultContent.Keys.Concatenate()}." );
            File.WriteAllText( KeyVaultPath, KeyVault.EncryptValuesToString( _vaultContent, _passPhrase ) );
        }

        /// <summary>
        /// Clears the current secrets that may exist in memory and the persisted key vault.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void DeleteKeyVault( IActivityMonitor m )
        {
            using var l = AcquireLock();

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
                using( AcquireLock() )
                {
                    if( IsKeyVaultOpened )
                    {
                        DoSaveKeyVault( null );
                        _passPhrase = null;
                    }
                }
            }
        }
    }
}
