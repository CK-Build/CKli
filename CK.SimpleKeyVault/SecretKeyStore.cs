using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.SimpleKeyVault
{
    /// <summary>
    /// Implements a simple <see cref="SecretKeyInfo"/> store.
    /// </summary>
    public class SecretKeyStore
    {
        readonly Dictionary<string, SecretKeyInfo> _keyInfos;
        readonly Dictionary<string, string> _undeclaredSecrets;
        readonly List<SecretKeyInfo> _orderedInfos;

        /// <summary>
        /// Opaque capture of a <see cref="SecretKeyStore"/> content.
        /// </summary>
        public struct Snapshot
        {
            readonly (string Name, string Description, string? Secret, bool IsRequired, string? SubKey, string? SourceProviderName)[] _data;

            /// <summary>
            /// Gets a "safe" data where the secret is only exposed as a bool.
            /// </summary>
            /// <returns>The safe data.</returns>
            public (string Name, string Description, bool SecretAvailable, bool IsRequired, string? SubKey, string? SourceProviderName)[] GetSafeData()
            {
                var result = new (string Name, string Description, bool SecretAvailable, bool IsRequired, string? SubKey, string? SourceProviderName)[_data.Length];
                for( int i = 0; i < _data.Length; ++i )
                {
                    ref var s = ref _data[i];
                    ref var t = ref result[i];
                    t.Name = s.Name;
                    t.Description = s.Description;
                    t.SecretAvailable = s.Secret != null;
                    t.IsRequired = s.IsRequired;
                    t.SourceProviderName = s.SourceProviderName;
                }
                return result;
            }

            internal Snapshot( SecretKeyStore store )
            {
                using var l = store.AcquireLock();
                _data = new (string name, string description, string? secret, bool isRequired, string? subKey, string? sourceProviderName)[store._orderedInfos.Count];
                for( int i = 0; i < store._orderedInfos.Count; i++ )
                {
                    _data[i] = store._orderedInfos[i].GetData();
                }
            }

            /// <summary>
            /// Restores this snapshot content into the <paramref name="store"/>.
            /// </summary>
            /// <param name="store">The target store.</param>
            public void RestoreTo( SecretKeyStore store )
            {
                using var l = store.AcquireLock();
                store.DoClear();
                int max = _data.Length - 1;
                for( int i = 0; i <= max; i++ )
                {
                    var s = new SecretKeyInfo( store, _data[max - i], store._keyInfos );
                    store._orderedInfos.Insert( 0, s );
                    store._keyInfos.Add( s.Name, s );
                }
            }
        }

        /// <summary>
        /// Initializes a new empty key store.
        /// </summary>
        public SecretKeyStore()
        {
            _keyInfos = new Dictionary<string, SecretKeyInfo>();
            _undeclaredSecrets = new Dictionary<string, string>();
            _orderedInfos = new List<SecretKeyInfo>();
        }

        /// <summary>
        /// Copy constructor. Initializes a copy key store.
        /// </summary>
        /// <param name="other">The source.</param>
        public SecretKeyStore( SecretKeyStore other )
            : this()
        {
            other.CreateSnapshot().RestoreTo( this );
        }

        internal IDisposable AcquireLock()
        {
            System.Threading.Monitor.Enter( _undeclaredSecrets );
            return Util.CreateDisposableAction( () => System.Threading.Monitor.Exit( _undeclaredSecrets ) );
        }

        /// <summary>
        /// Raised each time a secret is declared.
        /// </summary>
        public event EventHandler<SecretKeyInfoDeclaredArgs>? SecretDeclared;

        /// <summary>
        /// Gets the list of <see cref="SecretKeyInfo"/> that have been declared with the
        /// guaranty that Super keys appear before their respective subordinated key.
        /// </summary>
        public IReadOnlyList<SecretKeyInfo> Infos => _orderedInfos;

        /// <summary>
        /// Gets a filtered list of available named secrets without useless subordinate keys.
        /// </summary>
        public IEnumerable<(string Name, string Secret)> OptimalNamedSecrets
        {
            get
            {
                using var l = AcquireLock();
                return _orderedInfos.Where( i => i.IsSecretAvailable && (i.SuperKey == null || !i.SuperKey.IsSecretAvailable) )
                             .Select( i => (i.Name, i.Secret!) )
                             .Concat( _undeclaredSecrets.Select( kv => (kv.Key, kv.Value) ) )
                             .ToList();
            }
        }

        /// <summary>
        /// Gets the secret key info or null if it doesn't exist.
        /// </summary>
        /// <param name="name">The name of the key.</param>
        /// <returns>The info or null.</returns>
        public SecretKeyInfo? Find( string name )
        {
            using var l = AcquireLock();
            return _keyInfos.GetValueOrDefault( name );
        }

        /// <summary>
        /// Creates a new <see cref="Snapshot"/>.
        /// </summary>
        /// <returns>The snapshot.</returns>
        public Snapshot CreateSnapshot() => new Snapshot( this );

        /// <summary>
        /// Clears this store.
        /// </summary>
        public void Clear()
        {
            using var l = AcquireLock();
            DoClear();
        }

        void DoClear()
        {
            _orderedInfos.Clear();
            _keyInfos.Clear();
            _undeclaredSecrets.Clear();
        }

        /// <summary>
        /// Declares a secret key.
        /// Can be called as many times as needed: the <paramref name="descriptionBuilder"/> can
        /// compose the final description and the <paramref name="isRequired"/> is combined with a 'or'
        /// (as soon as one secret is required, then it is required).
        /// </summary>
        /// <param name="name">The name of the secret key.</param>
        /// <param name="descriptionBuilder">Description builder function. Allow to manipulate the description.
        /// Other components may have already put some description.</param>
        /// <param name="isRequired">True if this key is required to initialize a World.</param>
        /// <returns>The secret key info.</returns>
        public SecretKeyInfo DeclareSecretKey( string name,
                                               Func<SecretKeyInfo?, string> descriptionBuilder,
                                               bool isRequired = false,
                                               string? sourceProviderName = null )
        {
            Throw.CheckNotNullArgument( name );
            bool redeclaration = true;
            SecretKeyInfo? info;
            using( AcquireLock() )
            {
                if( !_keyInfos.TryGetValue( name, out info ) )
                {
                    redeclaration = false;
                    _keyInfos.Add( name, info = new SecretKeyInfo( this, name, descriptionBuilder, isRequired, sourceProviderName ) );
                    _orderedInfos.Add( info );
                    LookupFromUndeclared( info );
                }
                else
                {
                    info.Reconfigure( descriptionBuilder, isRequired );
                }
            }
            SecretDeclared?.Invoke( this, new SecretKeyInfoDeclaredArgs( info, redeclaration ) );
            return info;
        }

        void LookupFromUndeclared( SecretKeyInfo info )
        {
            if( _undeclaredSecrets.TryGetValue( info.Name, out var known ) )
            {
                info.SetSecret( known );
                _undeclaredSecrets.Remove( info.Name );
            }
        }

        /// <summary>
        /// Declares a secret key.
        /// Can be called as many times as needed: the <paramref name="descriptionBuilder"/> can
        /// compose the final description as long as the <paramref name="subKey"/> is always the same (if
        /// the subKey differs, an <see cref="InvalidOperationException"/> is thrown).
        /// </summary>
        /// <param name="name">The name of the secret key.</param>
        /// <param name="descriptionBuilder">Description builder function.</param>
        /// <param name="subKey">The sub key.</param>
        /// <returns>The secret key info.</returns>
        public SecretKeyInfo DeclareSecretKey( string name,
                                               Func<SecretKeyInfo?, string> descriptionBuilder,
                                               SecretKeyInfo subKey,
                                               string? sourceNameMaxLength = null )
        {
            bool redeclaration = true;
            SecretKeyInfo? info;
            using( AcquireLock() )
            {
                if( !_keyInfos.TryGetValue( name, out info ) )
                {
                    redeclaration = false;
                    _keyInfos.Add( name, info = new SecretKeyInfo( this, name, descriptionBuilder, subKey, _keyInfos, sourceNameMaxLength ) );
                    _orderedInfos.Insert( 0, info );
                    LookupFromUndeclared( info );
                }
                else
                {
                    info.Reconfigure( descriptionBuilder, subKey );
                }
            }
            SecretDeclared?.Invoke( this, new SecretKeyInfoDeclaredArgs( info, redeclaration ) );
            return info;
        }

        /// <summary>
        /// Sets or clears a secret that may have been declared or not.
        /// Updates it when <paramref name="secret"/> is not null or empty, otherwise clears it.
        /// Returns true if secret has been changed, false otherwise.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="name">The secret name.</param>
        /// <param name="secret">The secret value. Null or empty clears the secret.</param>
        /// <returns>True if secret has been changed, false otherwise.</returns>
        public bool SetSecret( IActivityMonitor monitor, string name, string? secret )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            using var l = AcquireLock();
            bool clear = String.IsNullOrEmpty( secret );
            if( !_keyInfos.TryGetValue( name, out var info ) )
            {
                bool already = _undeclaredSecrets.ContainsKey( name );
                if( clear )
                {
                    if( _undeclaredSecrets.Remove( name ) )
                    {
                        monitor.Info( $"Secret '{name}' has been cleared." );
                    }
                    else
                    {
                        monitor.Warn( $"Secret'{name}' not found." );
                    }
                }
                else
                {
                    Debug.Assert( secret != null );
                    _undeclaredSecrets[name] = secret;
                    monitor.Warn( $"Secret '{name}' has been {(already ? "updated" : "registered")} but is not declared yet." );
                }
                return true;
            }
            if( info.IsSecretAvailable
                && info.SuperKey != null
                && info.SuperKey.IsSecretAvailable )
            {
                monitor.Error( $"Secret '{name}' is defined by its super key '{info.SuperKey.Name}'." );
                return false;
            }
            if( info.SetSecret( secret ) )
            {
                monitor.Info( $"Secret '{name}' has been {(clear ? "cleared" : "updated")}." );
                return true;
            }
            monitor.Trace( $"Secret '{name}' unchanged." );
            return false;
        }

        /// <summary>
        /// Gets whether a secret key has been declared (the returned is not null) and whether its
        /// secret is available or not.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret name.</param>
        /// <returns>Null if the secret has not been declared, false if it has been declared but not known.</returns>
        public bool? IsSecretKeyAvailable( string name )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            using var l = AcquireLock();
            if( !_keyInfos.TryGetValue( name, out var info ) ) return null;
            return info.IsSecretAvailable;
        }

        /// <summary>
        /// Imports a set of keys and their secrets.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="secrets">The secrets to import.</param>
        public void ImportSecretKeys( IActivityMonitor monitor, IReadOnlyDictionary<string, string?> secrets )
        {
            if( secrets == null ) throw new ArgumentException( nameof( secrets ) );
            using var l = AcquireLock();
            foreach( var info in Infos )
            {
                if( secrets.TryGetValue( info.Name, out var secret ) )
                {
                    if( !String.IsNullOrEmpty( secret ) )
                    {
                        info.ImportSecret( monitor, secret );
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves a secret.
        /// The name must have been declared first otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret name.</param>
        /// <param name="throwOnUnavailable">True to throw an exception if the secret cannot be obtained.</param>
        /// <returns>The secret or null if it's not available (and <paramref name="throwOnUnavailable"/> is false).</returns>
        public string? GetSecretKey( IActivityMonitor m, string name, [DoesNotReturnIf(true)]bool throwOnUnavailable )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            using var l = AcquireLock();
            if( !_keyInfos.TryGetValue( name, out SecretKeyInfo? keyInfo ) )
            {
                Throw.InvalidOperationException( $"Secret '{name}' must be declared before any use of it." );
            }
            if( !keyInfo.IsSecretAvailable && throwOnUnavailable )
            {
                string exceptionInfo = keyInfo.ToString();
                if( keyInfo.SuperKey != null )
                {
                    SecretKeyInfo? k = keyInfo.SuperKey;
                    do
                    {
                        exceptionInfo += "\nOr better: " + k.ToString();
                        k = k.SuperKey;
                    }
                    while( k != null );
                }
                throw new MissingRequiredSecretException( exceptionInfo );
            }
            return keyInfo.Secret;
        }


    }
}
