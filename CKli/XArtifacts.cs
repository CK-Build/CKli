using CK.Core;
using CK.Env;
using System.Linq;

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
            var feeds = initializer.Reader.WithRequiredChild( "SourceFeeds" ).WithChildren();
            var repositories = initializer.Reader.WithRequiredChild( "TargetRepositories" ).WithChildren();
            artifactCenter.Initialize( initializer.Monitor, feeds, repositories );
        }
    }
}
