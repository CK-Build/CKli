using CK.Core;
using System;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Concrete and immutable implementation of <see cref="INPMSource"/>.
    /// </summary>
    public class NPMSource : INPMSource
    {
        /// <summary>
        /// Initializes a new <see cref="NPMSource"/> from its xml representation.
        /// </summary>
        /// <param name="e">The xml element.</param>
        public NPMSource( XElement e )
        {
            Scope = (string)e.AttributeRequired( "Scope" );
            if( !Scope.StartsWith( "@" ) ) throw new Exception( $"Element '{e}': Scope must start with @." );
            Url = (string)e.AttributeRequired( "Url" );
            Credentials = e.Elements( "Credentials" )
                            .Select( c => new SimpleCredentials( c ) )
                            .FirstOrDefault();
        }

        /// <summary>
        /// Copy constructor.
        /// </summary>
        /// <param name="other">The other source information.</param>
        public NPMSource( INPMSource other )
        {
            Scope = other.Scope;
            Url = other.Url;
            Credentials = other.Credentials;
        }

        /// <summary>
        /// Gets the scope.
        /// Can not be null and always starts with @.
        /// </summary>
        public string Scope { get; }

        /// <summary>
        /// Gets the registry url.
        /// Can not be null.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Gets optional credentials for the source.
        /// Can be null.
        /// </summary>
        public SimpleCredentials Credentials { get; }

    }
}
