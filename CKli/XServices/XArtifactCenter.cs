using CK.Core;
using CK.Env;

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
            // Quick & dirty registration.
            ArtifactType.Register( "NuGet", true );
            ArtifactType.Register( "NPM", true );
            ArtifactType.Register( "CKSetup", false );
        }

        /// <summary>
        /// Gets the artifact type factory: <see cref="IArtifactRepositoryFactory"/> must be registered
        /// in it.
        /// This is injected into the <see cref="FileSystem.ServiceContainer"/>.
        /// </summary>
        public ArtifactCenter ArtifactCenter { get; }
    }
}
