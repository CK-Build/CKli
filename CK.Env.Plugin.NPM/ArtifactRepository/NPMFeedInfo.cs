using CK.Core;
using CK.Env;
using System;
using System.Xml.Linq;

namespace CK.Env.NPM
{
    public abstract class NPMFeedInfo : INPMFeedInfo
    {
        protected NPMFeedInfo( XElement e )
        {
            QualityFilter = new PackageQualityFilter( (string)e.Attribute( "QualityFilter" ) );
        }

        /// <summary>
        /// Gets the type of feed.
        /// </summary>
        public abstract NPMFeedType Type { get; }

        /// <summary>
        /// Gets the name of this feed.
        /// Name is used as the feed identifier: it must be unique accross a set of NPM feeds.
        /// (See <see cref="NPMFeedInfoComparer"/>.)
        /// </summary>
        public abstract string Name { get; }


        /// <summary>
        /// Gets the range of package quality that is accepted by this feed.
        /// </summary>
        public PackageQualityFilter QualityFilter { get; }

        string IArtifactRepositoryInfo.UniqueArtifactRepositoryName => ToString();

        /// <summary>
        /// Gets the secret key name.
        /// </summary>
        public abstract string SecretKeyName { get; }

        /// <summary>
        /// Overridden to return the <see cref="Type"/> and <see cref="Name"/>.
        /// This is the <see cref="IArtifactRepositoryInfo.UniqueArtifactRepositoryName"/>.
        /// </summary>
        /// <returns>A readble string.</returns>
        public override string ToString() => $"{Type}:{Name}";

        /// <summary>
        /// Creates a <see cref="INPMFeedInfo"/> from a <see cref="XElement"/>.
        /// </summary>
        /// <param name="e">The xml element.</param>
        /// <param name="skipMissingType">
        /// True to silently ignore an element that doesn't have a Type attribute whose value is one of <see cref="NPMFeedType"/>
        /// and returns null.
        /// When false (the default), an exception is thrown.
        /// </param>
        /// <returns>The info or null.</returns>
        public static INPMFeedInfo Create( XElement e, bool skipMissingType = false )
        {
            switch( e.AttributeEnum( "Type", NPMFeedType.None ) )
            {
                case NPMFeedType.NPMAzure: return new NPMAzureFeedInfo( e );
                case NPMFeedType.NPMStandard: return new NPMStandardFeedInfo( e );
                default:
                    if( !skipMissingType ) throw new Exception( $"Not a NPMFeedInfo element: {e}." );
                    return null;
            }
        }
    }
}
