using CK.Core;
using System.Xml.Linq;

namespace CK.NPMClient
{
    /// <summary>
    /// Immutable implementation of <see cref="INPMFeedInfo"/> for standard feeds
    /// where a secret push API key is required.
    /// </summary>
    public class NPMStandardFeedInfo : NPMFeedInfo
    {
        public NPMStandardFeedInfo( XElement e )
        {
            Name = (string)e.AttributeRequired( "Name" );
            Url = (string)e.AttributeRequired( "Url" );
            SecretKeyName = (string)e.AttributeRequired( "SecretKeyName" );
            UsePassword = (bool?)e.Attribute( "UsePassword" ) ?? false;
        }

        public override NPMFeedType Type => NPMFeedType.NPMStandard;

        public override string Name { get; }

        public string Url { get; }

        /// <summary>
        /// Gets whether password authentication must be used (ie. "registry:_password=..." in .npmrc).
        /// Defaults to false: uses "registry:_authToken=..." in .npmrc.
        /// </summary>
        public bool UsePassword { get; }

        public override string SecretKeyName { get; }

    }
}
