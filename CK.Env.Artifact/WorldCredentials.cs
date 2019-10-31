using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public class WorldCredentials
    {
        /// <summary>
        /// Initialize a new world credential.
        /// </summary>
        public WorldCredentials(string username, string secretKeyName, string secretValue)
        {
            UserName = username;
            PasswordSecretKeyName = secretKeyName;
            Password = secretValue;
        }

        /// <summary>
        /// Initialize a new <see cref="WorldCredentials"/> from its xml representation
        /// that must have an attribute for the UserName, Password, and the SecretKeyName. 
        /// </summary>
        /// <param name="r">The xml element reader.</param>
        public WorldCredentials(XElementReader r)
        {
            UserName = r.HandleRequiredAttribute<string>( "UserName" );
            Password = r.HandleRequiredAttribute<string>( "Password" );
            PasswordSecretKeyName = r.HandleRequiredAttribute<string>( "PasswordSecretKeyName" );
        }

        /// <summary>
        /// Gets the Xml representation of this credentials.
        /// </summary>
        public XElement ToXml() => new XElement( "Credentials",
                                        new XAttribute( "UserName", UserName ),
                                        new XAttribute( "Password", Password ),
                                        new XAttribute( "PasswordSecretKeyName", PasswordSecretKeyName ) );

        /// <summary>
        /// Gets the User name.
        /// </summary>
        public string UserName { get; }

        /// <summary>
        /// Gets the Secret Key Name.
        /// </summary>
        public string PasswordSecretKeyName { get; }

        /// <summary>
        /// Gets the Secret value.
        /// </summary>
        public string Password { get; }
    }
}
