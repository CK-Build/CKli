using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CK.SimpleKeyVault
{
    /// <summary>
    /// Defines a key that can be associated to a secret.
    /// </summary>
    public class SecretKeyInfo
    {
        readonly SecretKeyStore _store;
        string? _secret;
        string _description;
        bool _isRequired;

        internal SecretKeyInfo( SecretKeyStore store,
                                string name,
                                Func<SecretKeyInfo?, string> descriptionBuilder,
                                bool isRequired,
                                string? sourceProviderName )
            : this( store, name, descriptionBuilder, sourceProviderName )
        {
            _isRequired = isRequired;
        }

        internal SecretKeyInfo( SecretKeyStore store,
                                string name,
                                Func<SecretKeyInfo?, string> descriptionBuilder,
                                SecretKeyInfo subKey,
                                IReadOnlyDictionary<string, SecretKeyInfo> keyInfos,
                                string? sourceProviderName )
            : this( store, name, descriptionBuilder, sourceProviderName )
        {
            Throw.CheckNotNullArgument( subKey );
            if( subKey.SuperKey != null )
            {
                Throw.ArgumentException( $"SubKey '{subKey.Name}' is already bound to SuperKey '{subKey.SuperKey.Name}'." );
            }
            if( !keyInfos.TryGetValue( subKey.Name, out var exists ) || exists != subKey )
            {
                Throw.ArgumentException( $"SubKey '{subKey.Name}' is not found in store." );
            }
            SubKey = subKey;
            subKey.SuperKey = this;
        }

        /// <summary>
        /// Deserialization constructor.
        /// </summary>
        /// <param name="data">Data captured by <see cref="GetData"/>.</param>
        /// <param name="keyInfos">Current set of secrets being restored.</param>
        internal SecretKeyInfo( SecretKeyStore store,
                                (string name, string description, string? secret, bool isRequired, string? subKey, string? sourceProviderName) data,
                                IReadOnlyDictionary<string, SecretKeyInfo> keyInfos )
        {
            _store = store;
            SourceProviderName = data.sourceProviderName;
            Name = data.name;
            _description = data.description;
            _secret = data.secret;
            _isRequired = data.isRequired;
            if( data.subKey != null )
            {
                SubKey = keyInfos[data.subKey];
                SubKey.SuperKey = this;
            }
        }

        /// <summary>
        /// Exports data for "serialization".
        /// </summary>
        /// <returns>The raw data.</returns>
        internal (string name, string description, string? secret, bool isRequired, string? subKey, string? sourceProviderName) GetData()
        {
            return (Name, _description, _secret, _isRequired, SubKey?.Name, SourceProviderName);
        }

        SecretKeyInfo( SecretKeyStore store, string name, Func<SecretKeyInfo?, string> descriptionBuilder, string? sourceProviderName )
        {
            Throw.CheckNotNullOrEmptyArgument( name );
            _store = store;
            SourceProviderName = sourceProviderName;
            Name = name;
            _description = String.Empty;
            SetDescription( descriptionBuilder );
        }

        internal void Reconfigure( Func<SecretKeyInfo?, string> descriptionBuilder, bool isRequired )
        {
            SetDescription( descriptionBuilder );
            _isRequired |= isRequired;
            CheckSecretPropagation();
        }

        internal void Reconfigure( Func<SecretKeyInfo?, string> descriptionBuilder, SecretKeyInfo subKey )
        {
            SetDescription( descriptionBuilder );
            if( subKey != SubKey )
            {
                Throw.InvalidOperationException( $"Invalid SubKey specification '{subKey?.Name ?? "null"}': Key '{Name}' is bound to '{SubKey?.Name ?? "null"}'." );
            }
            CheckSecretPropagation();
        }

        void SetDescription( Func<SecretKeyInfo?, string> descriptionBuilder )
        {
            Throw.CheckNotNullArgument( descriptionBuilder );
            _description = descriptionBuilder( String.IsNullOrWhiteSpace( _description ) ? null : this );
            Throw.CheckState( !String.IsNullOrWhiteSpace( _description ), "Description must not be null or empty." );
        }

        /// <summary>
        /// Gets the name of this secret key.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the source provider: when a secret is provided by a source, the key is transient and
        /// will not be stored in a KeyVault.
        /// </summary>
        public string? SourceProviderName { get; }

        /// <summary>
        /// Gets the description. Defaults to <see cref="String.Empty"/>.
        /// </summary>
        public string Description => _description;

        /// <summary>
        /// Gets whether this key is required.
        /// </summary>
        public bool IsRequired => _isRequired;

        /// <summary>
        /// Gets whether this key has its associated secret available.
        /// </summary>
        [MemberNotNullWhen(true,nameof(Secret))]
        public bool IsSecretAvailable => _secret != null;

        /// <summary>
        /// Gets a non empty secret or null if <see cref="IsSecretAvailable"/> is false.
        /// (Note that this secret may be defined by the <see cref="SuperKey"/>.)
        /// </summary>
        public string? Secret => _secret;

        [Conditional( "DEBUG" )]
        void CheckSecretPropagation()
        {
            bool isValidNull = _secret == null && (SuperKey == null || !SuperKey.IsSecretAvailable);
            bool isValidAvailable = _secret != null && (SuperKey?._secret == null || SuperKey._secret == _secret);
            Debug.Assert( isValidNull || isValidAvailable );
        }

        /// <summary>
        /// Imports a secret, typically stored in an external safe place.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="secret">The secret to set. Must not be null or empty.</param>
        /// <returns>True if the secret has been updated, false if nothing has changed.</returns>
        internal bool ImportSecret( IActivityMonitor m, string secret )
        {
            Throw.CheckNotNullOrEmptyArgument( secret );
            if( !string.IsNullOrWhiteSpace( SourceProviderName ) ) Throw.InvalidOperationException( $"This secret is provided by '{SourceProviderName}' you cannot import a transient secret." );
            if( IsSecretAvailable )
            {
                if( SuperKey != null && SuperKey.IsSecretAvailable )
                {
                    m.Warn( $"Secret '{Name}' already set by it SuperKey ('{SuperKey.Name}')." );
                }
                else if( SetSecret( secret ) )
                {
                    m.Info( $"Imported secret '{Name}' has replaced the previous one." );
                    return true;
                }
            }
            else if( SetSecret( secret ) )
            {
                m.Info( $"Imported secret '{Name}'." );
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sets the secret for this key and its <see cref="SubKey"/> (recursively) if any.
        /// If the current secret is provided by the <see cref="SuperKey"/>, this
        /// raises an <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <param name="secret">The secret to set. Null or empty to clear it.</param>
        /// <returns>True if a secret has been updated, false if nothing has changed.</returns>
        public bool SetSecret( string? secret )
        {
            using var l = _store.AcquireLock();
            CheckSecretPropagation();
            if( String.IsNullOrEmpty( secret ) ) secret = null;
            if( SuperKey != null && SuperKey.IsSecretAvailable )
            {
                throw new InvalidOperationException( $"Secret is available at the SuperKey '{SuperKey.Name}' level. It cannot be set on '{Name}'." );
            }
            bool changed = false;
            SecretKeyInfo? k = this;
            do
            {
                changed |= k._secret != secret;
                k._secret = secret;
                k = k.SubKey;
            }
            while( k != null );
            return changed;
        }

        /// <summary>
        /// Gets the secret key that is more powerful than this one or null if no such key exists.
        /// This super key if it exists and if its <see cref="IsSecretAvailable"/> is true, overrides the secret at this level.
        /// </summary>
        public SecretKeyInfo? SuperKey { get; private set; }

        /// <summary>
        /// Gets the secret key that is covered by this one or null if no such key exists.
        /// This subordinated key's secret is replaced by this one if it is set.
        /// </summary>
        public SecretKeyInfo? SubKey { get; }

        /// <summary>
        /// Gets the <see cref="Name"/>, <see cref="Description"/>, <see cref="IsRequired"/> and <see cref="IsSecretAvailable"/> status.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"Secret '{Name}' [{(IsRequired ? "Required" : "Optional")},{(IsSecretAvailable ? "Available" : "Unavailable")}]: {_description}";

    }
}
