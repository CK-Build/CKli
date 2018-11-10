using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.NuGetClient
{
    public abstract class NuGetFeedInfo : INuGetFeedInfo
    {
        /// <summary>
        /// Gets the type of feed.
        /// </summary>
        public abstract NuGetFeedType Type { get; }

        /// <summary>
        /// Gets the name of this feed.
        /// Name is used as the feed identifier: it must be unique accross a set of feeds.
        /// (See <see cref="NuGetFeedInfoComparer"/>.)
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Overridden to return the <see cref="Type"/> and <see cref="Name"/>.
        /// </summary>
        /// <returns>A readble string.</returns>
        public override string ToString() => $"{Type}: {Name}";

        /// <summary>
        /// Creates a <see cref="INuGetFeedInfo"/> from a <see cref="XElement"/>.
        /// </summary>
        /// <param name="e">The xml element.</param>
        /// <param name="skipMissingType">True to throw an exception if the element doesn't have a Type attribute.</param>
        /// <returns>The info or null.</returns>
        public static INuGetFeedInfo Create( XElement e, bool skipMissingType = false )
        {
            switch( e.AttributeEnum( "Type", NuGetFeedType.None ) )
            {
                case NuGetFeedType.Azure: return new NuGetAzureFeedInfo( e );
                case NuGetFeedType.Standard: return new NuGetStandardFeedInfo( e );
                default:
                    if( !skipMissingType ) throw new Exception( $"Not a NuGetFeedInfo element: {e}." );
                    return null;
            }
        }
    }
}
