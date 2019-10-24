using CK.Core;
using CK.Env;

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
            var feeds = initializer.Reader.WithRequiredChild( "SourceFeeds" ).WithChildren( true );
            var repositories = initializer.Reader.WithRequiredChild( "TargetRepositories" ).WithChildren( true );
            artifactCenter.Initialize( initializer.Monitor, feeds, repositories );
        }
    }
}
