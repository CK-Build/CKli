using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class XArtifactRepositories : XTypedObject
    {
        public XArtifactRepositories(
            Initializer initializer,
            XArtifactCenter artifactCenter )
            : base( initializer )
        {
            initializer.Services.Add( this );
            artifactCenter.ArtifactCenter.InstanciateRepositories( initializer.Monitor, initializer.Element.Elements( "Repository" ) );
        }
    }
}
