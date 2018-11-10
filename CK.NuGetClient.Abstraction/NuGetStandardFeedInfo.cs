using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.NuGetClient
{
    /// <summary>
    /// Immutable implementation of <see cref="INuGetFeedInfo"/> for standard feeds
    /// where a secret push API key is required.
    /// </summary>
    public class NuGetStandardFeedInfo : INuGetFeedInfo
    {
        public NuGetStandardFeedInfo( XElement e )
        {
            Name = (string)e.AttributeRequired( "Name" );
            Url = (string)e.AttributeRequired( "Url" );
            SecretKeyName = (string)e.AttributeRequired( "SecretKeyName" );
        }

        public NuGetFeedType Type => NuGetFeedType.Standard;

        public string Name { get; }

        public string Url { get; }

        public string SecretKeyName { get; }

        public override string ToString() => $"{Type}: {Name} -> {Url}";

    }
}
