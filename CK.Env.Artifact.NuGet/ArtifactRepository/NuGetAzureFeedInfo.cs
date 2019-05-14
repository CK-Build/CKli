using CK.Core;
using System.Xml.Linq;

namespace CK.Env.NuGet
{
    /// <summary>
    /// Immutable implementation of <see cref="INuGetFeedInfo"/> for Azure feeds.
    /// </summary>
    public class NuGetAzureFeedInfo : NuGetFeedInfo
    {
        readonly string _name;

        public NuGetAzureFeedInfo( XElement e )
        {
            Organization = (string)e.AttributeRequired( "Organization" );
            FeedName = (string)e.AttributeRequired( "FeedName" );
            var label = (string)e.Attribute( "Label" );
            _name = Organization + '-' + FeedName;
            if( label != null ) _name += '-' + label;
            if( label != null ) label = "@" + label;
            Label = label;
        }

        public override NuGetFeedType Type => NuGetFeedType.NuGetAzure;

        /// <summary>
        /// Gets the name of this feed:
        /// <see cref="Organization"/>-<see cref="FeedName"/>[-<see cref="Label"/>(without the '@' label prefix)].
        /// </summary>
        public override string Name => _name;

        /// <summary>
        /// The secret key name is:
        /// "AZURE_FEED_" + Organization.ToUpperInvariant().Replace( '-', '_' ).Replace( ' ', '_' ) + "_PAT".
        /// </summary>
        public override string SecretKeyName => "AZURE_FEED_"
                                                + Organization
                                                      .ToUpperInvariant()
                                                      .Replace( '-', '_' )
                                                      .Replace( ' ', '_' )
                                                + "_PAT";

        /// <summary>
        /// Gets the organization name.
        /// </summary>
        public string Organization { get; }

        /// <summary>
        /// Gets the name of the feed inside the <see cref="Organization"/>.
        /// </summary>
        public string FeedName { get; }

        /// <summary>
        /// Gets the "@Label" string or null.
        /// </summary>
        public string Label { get; }

        public string Url => $"https://pkgs.dev.azure.com/{Organization}/_packaging/{FeedName}{Label}/nuget/v3/index.json";
    }
}
