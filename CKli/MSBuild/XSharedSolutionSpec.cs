using CK.Core;
using CK.Env;
using CK.Env.DependencyModel;

namespace CKli
{

    /// <summary>
    /// Required shared solution specification.
    /// The first occurrence injects a <see cref="SolutionContext"/>
    /// </summary>
    public class XSharedSolutionSpec : XTypedObject
    {
        public XSharedSolutionSpec(
            Initializer initializer,
            XArtifactCenter artifactCenter,
            FileSystem fs,
            XSharedSolutionSpec previous = null )
            : base( initializer )
        {
            if( previous != null )
            {
                SharedSpec = new SharedSolutionSpec( previous.SharedSpec, artifactCenter.ArtifactCenter, initializer.Element );
                initializer.Services.Remove<XSharedSolutionSpec>();
            }
            else
            {
                SharedSpec = new SharedSolutionSpec( initializer.Element, artifactCenter.ArtifactCenter );
                new SolutionContext(); 
            }
            ArtifactCenter = artifactCenter.ArtifactCenter;
            initializer.Services.Add( this );
        }

        public ArtifactCenter ArtifactCenter { get; }

        public ISharedSolutionSpec SharedSpec { get; }

    }
}
