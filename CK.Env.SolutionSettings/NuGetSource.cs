using CK.Core;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Concrete and immutable implementation of <see cref="INuGetSource"/>.
    /// </summary>
    public class NuGetSource : INuGetSource
    {
        /// <summary>
        /// Initializes a new <see cref="NuGetSource"/> from its xml representation.
        /// </summary>
        /// <param name="e">The xml element.</param>
        public NuGetSource( XElement e )
        {
            Name = (string)e.AttributeRequired( "Name" );
            Url = (string)e.AttributeRequired( "Url" );
            Credentials = e.Elements( "Credentials" )
                            .Select( c => new SimpleCredentials( (string)c.AttributeRequired( "UserName" ), (string)c.AttributeRequired( "Password" ) ) )
                            .FirstOrDefault();
        }

        /// <summary>
        /// Compy constructor.
        /// </summary>
        /// <param name="other">The other source information.</param>
        public NuGetSource( INuGetSource other )
        {
            Name = other.Name;
            Url = other.Url;
            Credentials = other.Credentials;
        }

        /// <summary>
        /// Gets the feed name.
        /// Can not be null.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the feed url.
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
