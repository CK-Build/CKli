using CK.Core;
using System.Xml.Linq;

namespace CK.NuGetClient
{
    /// <summary>
    /// Immutable implementation of <see cref="INuGetFeedInfo"/> for standard feeds
    /// where a secret push API key is required.
    /// </summary>
    public class NuGetStandardFeedInfo : NuGetFeedInfo
    {
        public NuGetStandardFeedInfo( XElement e )
        {
            Name = (string)e.AttributeRequired( "Name" );
            Url = (string)e.AttributeRequired( "Url" );
            SecretKeyName = (string)e.AttributeRequired( "SecretKeyName" );
        }

        public override NuGetFeedType Type => NuGetFeedType.NuGetStandard;

        public override string Name { get; }

        public string Url { get; }

        public override string SecretKeyName { get; }

    }
}
