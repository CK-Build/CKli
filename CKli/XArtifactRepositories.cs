using CK.Core;
using CK.Env;

namespace CKli
{
    public class XArtifactRepositories : XTypedObject
    {
        public XArtifactRepositories(
            Initializer initializer,
            ArtifactCenter artifactCenter )
            : base( initializer )
        {
            initializer.Services.Add( this );
            artifactCenter.InstanciateRepositories( initializer.Monitor, initializer.Element.Elements( "Repository" ) );
        }
    }
}
