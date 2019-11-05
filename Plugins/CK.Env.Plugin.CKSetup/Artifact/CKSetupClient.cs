using CK.Core;
using CKSetup;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CK.Env.CKSetup
{
    public class CKSetupClient : IArtifactTypeHandler
    {
        /// <summary>
        /// Exposes the "CKSetup" <see cref="ArtifactType"/>.
        /// This type of artifact is not installable.
        /// </summary>
        public static readonly ArtifactType CKSetupType = ArtifactType.Register( "CKSetup", false );

        readonly HttpClient _sharedHttpClient;
        readonly SecretKeyStore _keyStore;

        public CKSetupClient( SecretKeyStore keyStore, HttpClient sharedHttpClient )
        {
            _keyStore = keyStore;
            _sharedHttpClient = sharedHttpClient;
        }

        public IArtifactRepository CreateRepository( in XElementReader r )
        {
            CKSetupStore store = null;
            string type = r.HandleOptionalAttribute<string>( "Type", null );
            if( type == "CKSetup" )
            {
                string url = r.HandleOptionalAttribute<string>( "Url", null );
                if( url == null ) store = new CKSetupStore( _keyStore, _sharedHttpClient );
                else if( !Uri.TryCreate( url, UriKind.Absolute, out var uri ) )
                {
                    r.ThrowXmlException( $"Invalid store url '{url}'." );
                }
                else
                {
                    if( uri == Facade.DefaultStoreUrl ) store = new CKSetupStore( _keyStore, _sharedHttpClient );
                    else
                    {
                        string name = r.HandleRequiredAttribute<string>( "Name" );
                        if( !Regex.IsMatch( name, "^\\w+$", RegexOptions.CultureInvariant ) )
                        {
                            r.ThrowXmlException( $"Invalid name. Must be an identifier ('^\\w+$' regex)." );
                        }
                        if( name == "Public" ) r.ThrowXmlException( $"'Public' name is reserved for the default public store." );
                        store = new CKSetupStore( _keyStore, _sharedHttpClient, uri, name );
                    }
                }
            }
            return store;
        }

        public IArtifactFeed CreateFeedFromXML(
            IActivityMonitor m,
            in XElementReader r,
            IReadOnlyList<IArtifactRepository> repositories,
            IReadOnlyList<IArtifactFeed> feeds )
        {
            return null;
        }

    }
}
