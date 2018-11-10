using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.NuGetClient
{
    /// <summary>
    /// Immutable implementation of <see cref="INuGetFeedInfo"/> for Azure feeds.
    /// </summary>
    public class NuGetAzureFeedInfo : INuGetFeedInfo
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

        public NuGetFeedType Type => NuGetFeedType.Azure;

        public string Name => _name;

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

        public override string ToString() => $"{Type}: {_name}";
    }
}
