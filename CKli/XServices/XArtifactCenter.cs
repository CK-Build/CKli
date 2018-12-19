using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class XArtifactCenter : XTypedObject
    {
        public XArtifactCenter(
            Initializer initializer,
            FileSystem fs )
            : base( initializer )
        {
            initializer.Services.Add( this );
            ArtifactCenter = new ArtifactCenter();
            fs.ServiceContainer.Add( ArtifactCenter );
        }

        /// <summary>
        /// Gets the artifact type factory: <see cref="IArtifactTypeFactory"/> must be registered
        /// in it.
        /// This is injected into the <see cref="FileSystem.ServiceContainer"/>.
        /// </summary>
        public ArtifactCenter ArtifactCenter { get; }
    }
}
