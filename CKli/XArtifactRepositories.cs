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
            initializer.HandledObjects.AddRange( repositories );
            artifactCenter.InstanciateRepositories( initializer.Monitor, repositories );
        }
    }
}
