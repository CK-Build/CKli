using System;
using System.Collections.Generic;
using System.Linq;
using CK.Core;

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
        /// Initializes a new empty key store.
        /// </summary>
        public SecretKeyStore()
        {
            _keyInfos = new Dictionary<string, SecretKeyInfo>();
            _orderedInfos = new List<SecretKeyInfo>();
        }

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
        /// <param name="descriptionBuilder">Description builder function.</param>
        /// <param name="isRequired">True if this key is required to initialize a World.</param>
        /// <returns>The secret key info.</returns>
        public SecretKeyInfo DeclareSecretKey( string name, Func<string, string> descriptionBuilder, bool isRequired = false )
        {
            if( !_keyInfos.TryGetValue( name, out var info ) )
            {
                _keyInfos.Add( name, info = new SecretKeyInfo( name, descriptionBuilder, isRequired ) );
                _orderedInfos.Add( info );
            }
            else info.Reconfigure( descriptionBuilder, isRequired );
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
        public SecretKeyInfo DeclareSecretKey( string name, Func<string, string> descriptionBuilder, SecretKeyInfo subKey )
        {
            if( !_keyInfos.TryGetValue( name, out var info ) )
            {
                _keyInfos.Add( name, info = new SecretKeyInfo( name, descriptionBuilder, subKey, _keyInfos ) );
                _orderedInfos.Insert( 0, info );
            }
            else info.Reconfigure( descriptionBuilder, subKey );
            return info;
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
            Dictionary<string, string> remainder = new Dictionary<string, string>();
            foreach( var info in Infos )
            {
                if( secrets.TryGetValue( info.Name, out var secret ) )
                {
                    if( info.IsSecretAvailable )
                    {
                        if( info.SuperKey != null && info.SuperKey.IsSecretAvailable )
                        {
                            m.Info( $"Secret '{info.Name}' already set by it SuperKey ('{info.SuperKey.Name}')." );
                        }
                        else
                        {
                            info.SetSecret( secret );
                            m.Info( $"Imported secret '{info.Name}' has replaced the previous one." );
                        }
                    }
                    else
                    {
                        info.SetSecret( secret );
                        m.Info( $"Imported secret '{info.Name}'." );
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
        public string GetSecretKey( IActivityMonitor m, string name, bool throwOnUnavailable )
        {
            if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentException( nameof( name ) );
            if( !_keyInfos.TryGetValue( name, out var info ) )
            {
                throw new InvalidOperationException( $"Secret '{name}' is not declared. It cannot be obtained." );
            }
            if( !info.IsSecretAvailable && throwOnUnavailable ) throw new Exception( info.ToString() );
            return info.Secret;
        }


    }
}
