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
            var repositories = initializer.Element.Elements( "Repository" ).ToList();
            initializer.HandledElements.AddRange( repositories );
            artifactCenter.InstanciateRepositories( initializer.Monitor, repositories );
        }
    }
}
