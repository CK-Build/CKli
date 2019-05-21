using CK.Core;
using CK.Env;
using System.Linq;

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
            var readers = initializer.Element
                            .Elements( "Repository" )
                            .Select( e => initializer.Reader.WithElement( e ) );
            artifactCenter.InstanciateRepositories( readers );
        }
    }
}
