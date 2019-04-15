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
        }

        public override NPMFeedType Type => NPMFeedType.NPMStandard;

        public override string Name { get; }

        public string Url { get; }

        public override string SecretKeyName { get; }

    }
}
