using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates shared <see cref="XDocument"/> state.
    /// </summary>
    public class SharedWorldState : BaseWorldState
    {
        readonly XElement _cidKeyVault;
        readonly XElement _artifactCache;

        public SharedWorldState( WorldStore store, IWorldName w, XDocument d = null )
            : base( store, w, false, d )
        {
            var r = XDocument.Root;
            _cidKeyVault = r.EnsureElement( XmlNames.xCICDKeyVault );
            _artifactCache = r.EnsureElement( XmlNames.xArtifactCache );
        }

        /// <summary>
        /// Gets or sets the central CI/CD key vault.
        /// It is protected by the CODECAKEBUILDER_SECRET_KEY.
        /// </summary>
        public string CICDKeyVault
        {
            get => _cidKeyVault.Value;
            set
            {
                if( value != _cidKeyVault.Value )
                {
                    _cidKeyVault.SetValue( value );
                }
            }
        }

        /// <summary>
        /// Stores the cached artifact informations in base64.
        /// </summary>
        public string ArtifactCache
        {
            get => _artifactCache.Value;
            set
            {
                if( value != _artifactCache.Value )
                {
                    _artifactCache.SetValue( value );
                }
            }
        }
    }
}
