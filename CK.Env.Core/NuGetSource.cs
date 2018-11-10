using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    public class NuGetSource : INuGetSource
    {
        public NuGetSource( XElement e )
        {
            Name = (string)e.AttributeRequired( "Name" );
            Url = (string)e.AttributeRequired( "Url" );
            Credentials = e.Elements( "Credentials" )
                            .Select( c => new SimpleCredentials( (string)c.AttributeRequired( "UserName" ), (string)c.AttributeRequired( "Password" ) ) )
                            .FirstOrDefault();
        }

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
