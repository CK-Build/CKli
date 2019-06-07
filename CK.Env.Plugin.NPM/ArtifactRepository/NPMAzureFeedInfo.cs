using CK.Core;
using System;
using System.Xml.Linq;

namespace CK.Env.NPM
{
    /// <summary>
    /// Immutable implementation of <see cref="INPMArtifactRepositoryInfo"/> for Azure feeds.
    /// </summary>
    public class NPMAzureFeedInfo : NPMArtifactRepositoryInfo
    {
        readonly string _name;

        public NPMAzureFeedInfo( in XElementReader r )
            : base( r )
        {
            Organization = r.HandleRequiredAttribute<string>( "Organization" );
            FeedName = r.HandleRequiredAttribute<string>( "FeedName" );
            NPMScope = r.HandleRequiredAttribute<string>( "NPMScope" );
            if( NPMScope.Length <= 1 || NPMScope[0] != '@' )
            {
                throw new Exception( $"'{r.Element.Name}'{r.Element.GetLineColumnString()}: invalid NPScope '{NPMScope}' (must start with a @)." );
            }
            _name = $"{NPMScope}->{Organization}-{FeedName}";
        }

        public override NPMFRepositoryType Type => NPMFRepositoryType.NPMAzure;

        /// <summary>
        /// Gets the name of this feed:
        /// <see cref="NPMScope"/>-><see cref="Organization"/>-<see cref="FeedName"/>.
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
        /// Gets the "@Scope" string: it MUST start with a @ and be a non empty scope name.
        /// </summary>
        public string NPMScope { get; }

        public string Url => $"https://pkgs.dev.azure.com/{Organization}/_packaging/{FeedName}/npm/registry/";
    }
}
