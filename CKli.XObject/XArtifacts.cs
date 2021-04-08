using CK.Core;
using CK.Env;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli
{
    public class XArtifacts : XTypedObject
    {
        public XArtifacts(
            Initializer initializer,
            ArtifactCenter artifactCenter )
            : base( initializer )
        {
            initializer.Services.Add( this );
            IEnumerable<XElementReader> feeds = initializer.Reader.WithRequiredChild( "SourceFeeds" ).WithChildren( true );
            IEnumerable<XElementReader> repositories = initializer.Reader.WithRequiredChild( "TargetRepositories" ).WithChildren( true );
            artifactCenter.Initialize( initializer.Monitor, feeds, repositories );
        }
    }
}
