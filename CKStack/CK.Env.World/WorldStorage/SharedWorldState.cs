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

        public SharedWorldState( IWorldStore store, IWorldName w, XDocument d = null )
            : base( store, w, false, d )
        {
            var r = XDocument.Root;
            _cidKeyVault = r.EnsureElement( XmlNames.xCICDKeyVault );
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
    }
}
