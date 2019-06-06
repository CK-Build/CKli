using System.Xml.Linq;

namespace CK.Env.NPM
{
    /// <summary>
    /// Immutable implementation of <see cref="INPMFeedInfo"/> for standard feeds
    /// where a secret push API key is required.
    /// </summary>
    public class NPMStandardFeedInfo : NPMFeedInfo
    {
        public NPMStandardFeedInfo( in XElementReader r )
            : base( r )
        {
            Name = r.HandleRequiredAttribute<string>( "Name" );
            Url = r.HandleRequiredAttribute<string>( "Url" );
            SecretKeyName = r.HandleRequiredAttribute<string>( "SecretKeyName" );
            UsePassword = r.HandleOptionalAttribute( "UsePassword", false );
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
