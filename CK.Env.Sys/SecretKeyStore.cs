using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Implements a simple <see cref="SecretKeyInfo"/> store.
    /// </summary>
    public class SecretKeyStore
    {
        readonly Dictionary<string, SecretKeyInfo> _keyInfos;
        readonly List<SecretKeyInfo> _orderedInfos;

        /// <summary>
        /// Opaque capture of a <see cref="SecretKeyStore"/> content.
        /// </summary>
        public struct Snapshot
        {
            readonly (string name, string description, string? secret, bool isRequired, string tags, string? subKey, string? sourceProviderName)[] _data;

            internal Snapshot( SecretKeyStore store )
            {
                _data = new (string name, string description, string? secret, bool isRequired, string tags, string? subKey, string? sourceProviderName)[store._orderedInfos.Count];
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
                store.Clear();
                int max = _data.Length - 1;
                for( int i = 0; i <= max; i++ )
                {
                    var s = new SecretKeyInfo( _data[max - i], store._keyInfos );
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
            _orderedInfos = new List<SecretKeyInfo>();
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
        /// Gets a filtered list of <see cref="SecretKeyInfo"/> for which secret is available and
        /// without useless subordinate keys.
        /// </summary>
        public IEnumerable<SecretKeyInfo> OptimalAvailableInfos => _orderedInfos.Where( i => i.IsSecretAvailable && (i.SuperKey == null || !i.SuperKey.IsSecretAvailable) );

        /// <summary>
        /// Gets the secret key info or null if it doesn't exist.
        /// </summary>
        /// <param name="name">The name of the key.</param>
        /// <returns>The info or null.</returns>
        public SecretKeyInfo? Find( string name ) => _keyInfos.GetValueOrDefault( name );

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
            _orderedInfos.Clear();
            _keyInfos.Clear();
        }

        /// <summary>
        /// Declares a secret key.
        /// Can be called as many times as needed: the <paramref name="descriptionBuilder"/> can
        /// compose the final description and the <paramref name="isRequired"/> is or'ed.
        /// </summary>
        /// <param name="name">The name of the secret key.</param>
        /// <param name="descriptionBuilder">Description builder function. Allow to manipulate the description.
        /// Other components may have already put some description.</param>
        /// <param name="isRequired">True if this key is required to initialize a World.</param>
        /// <returns>The secret key info.</returns>
        public SecretKeyInfo DeclareSecretKey( string name, Func<SecretKeyInfo?, string> descriptionBuilder, bool isRequired = false, string? sourceProviderName = null )
        {
            bool redeclaration = true;
            if( !_keyInfos.TryGetValue( name, out var info ) )
            {
                redeclaration = false;
                _keyInfos.Add( name, info = new SecretKeyInfo( name, descriptionBuilder, isRequired, sourceProviderName ) );
                _orderedInfos.Add( info );
            }
            else
            {
                info.Reconfigure( descriptionBuilder, isRequired );
            }

            SecretDeclared?.Invoke( this, new SecretKeyInfoDeclaredArgs( info, redeclaration ) );
            return info;
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
        public SecretKeyInfo DeclareSecretKey( string name, Func<SecretKeyInfo?, string> descriptionBuilder, SecretKeyInfo subKey, string? sourceNameMaxLength = null )
        {
            bool redeclaration = true;
            if( !_keyInfos.TryGetValue( name, out var info ) )
            {
                redeclaration = false;
                _keyInfos.Add( name, info = new SecretKeyInfo( name, descriptionBuilder, subKey, _keyInfos, sourceNameMaxLength ) );
                _orderedInfos.Insert( 0, info );
            }
            else
            {
                info.Reconfigure( descriptionBuilder, subKey );
            }

            SecretDeclared?.Invoke( this, new SecretKeyInfoDeclaredArgs( info, redeclaration ) );
            return info;
        }

        /// <summary>
        /// Sets or clears a secret that must have been declared.
        /// Updates it when <paramref name="secret"/> is not null or empty, otherwise clears it.
        /// Returns true if secret has been changed, false otherwise.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret name.</param>
        /// <param name="secret">The secret value. Null or empty clears the secret.</param>
        /// <returns>True if secret has been changed, false otherwise.</returns>
        public bool SetSecret( IActivityMonitor m, string name, string secret )
        {
            if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentNullException( nameof( name ) );
            bool clear = String.IsNullOrEmpty( secret );
            if( !_keyInfos.TryGetValue( name, out var info ) )
            {
                m.Error( $"Secret '{name}' is not declared." );
                return false;
            }
            if( info.IsSecretAvailable
                && info.SuperKey != null
                && info.SuperKey.IsSecretAvailable )
            {
                m.Error( $"Secret '{name}' is defined by its super key '{info.SuperKey.Name}'." );
                return false;
            }
            if( info.SetSecret( secret ) )
            {
                m.Info( $"Secret '{name}' has been {(clear ? "cleared" : "updated")}." );
                return true;
            }
            m.Trace( $"Secret '{name}' unchanged." );
            return false;
        }



        /// <summary>
        /// Gets whether a secret key has been declared (the returned is not null) and if whether the
        /// secret is available or not.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret name.</param>
        /// <returns>Null if the secret has not been declared, false if it has been declared but not known.</returns>
        public bool? IsSecretKeyAvailable( string name )
        {
            if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentException( nameof( name ) );
            if( !_keyInfos.TryGetValue( name, out var info ) ) return null;
            return info.IsSecretAvailable;
        }

        /// <summary>
        /// Imports a set of keys and their secrets.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="secrets">The secrets to import.</param>
        public void ImportSecretKeys( IActivityMonitor m, IReadOnlyDictionary<string, string> secrets )
        {
            if( secrets == null ) throw new ArgumentException( nameof( secrets ) );
            foreach( var info in Infos )
            {
                if( secrets.TryGetValue( info.Name, out var secret ) )
                {
                    info.ImportSecret( m, secret );
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
        public string? GetSecretKey( IActivityMonitor m, string name, bool throwOnUnavailable )
        {
            if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentException( nameof( name ) );
            if( !_keyInfos.TryGetValue( name, out SecretKeyInfo? keyInfo ) )
            {
                throw new InvalidOperationException( $"Secret '{name}' must be declared before any use of it." );
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
                    } while( k != null );
                }
                throw new MissingRequiredSecretException( exceptionInfo );
            }
            return keyInfo.Secret;
        }


    }

    public class MissingRequiredSecretException : Exception
    {
        public MissingRequiredSecretException( string message ) : base( message )
        {
        }
    }
}
