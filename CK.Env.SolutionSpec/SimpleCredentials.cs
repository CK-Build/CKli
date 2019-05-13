using CK.Core;
using System;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates simple credentials.
    /// <see cref="Password"/> can be the raw password or be the name of the actual password in
    /// an external vault.
    /// </summary>
    public class SimpleCredentials
    {
        /// <summary>
        /// Initializes a new simple credential.
        /// </summary>
        /// <param name="userName">The user name.</param>
        /// <param name="passwordOrSecretKeyName">The password or its name (a key to resolve it).</param>
        /// <param name="isSecretKeyName">
        /// True if the <paramref name="passwordOrSecretKeyName"/> is a name that must be resolved.
        /// </param>
        public SimpleCredentials( string userName, string passwordOrSecretKeyName, bool isSecretKeyName )
        {
            UserName = userName;
            PasswordOrSecretKeyName = passwordOrSecretKeyName;
            IsSecretKeyName = isSecretKeyName;
        }

        /// <summary>
        /// Initializes a new <see cref="SimpleCredentials"/> from its xml representation
        /// that must have a UserName attribute and Password or PasswordSecretKeyName (but not both). 
        /// </summary>
        /// <param name="e">The xml element.</param>
        public SimpleCredentials( XElement e )
        {
            UserName = (string)e.AttributeRequired( "UserName" );
            var p = (string)e.Attribute( "Password" );
            var k = (string)e.Attribute( "PasswordSecretKeyName" );
            bool hasP = !String.IsNullOrEmpty( p );
            bool hasK = !String.IsNullOrEmpty( k );
            if( hasP && hasK ) throw new ArgumentException( $"Credential element '{e}' can not specify both Password and PasswordSecretKeyName attributes." );
            if( hasP || hasK )
            {
                PasswordOrSecretKeyName = hasP ? p : k;
                IsSecretKeyName = hasK;
            }
        }

        /// <summary>
        /// User name.
        /// </summary>
        public string UserName { get; }

        /// <summary>
        /// The password or its name (the key to resolve it).
        /// May be null if no password at all is provided.
        /// </summary>
        public string PasswordOrSecretKeyName { get; }

        /// <summary>
        /// Whether <see cref="PasswordOrSecretKeyName"/> is actually the name of the password and
        /// not the password itself.
        /// </summary>
        public bool IsSecretKeyName { get; }
    }
}
