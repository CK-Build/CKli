using CK.Core;
using CK.Env;
using CK.Env.DependencyModel;

namespace CKli
{

    /// <summary>
    /// Required shared solution specification.
    /// The first occurrence injects a <see cref="SharedSolutionSpec"/>, subsequent occurrences
    /// injects a modified clone.
    /// </summary>
    public class XSharedSolutionSpec : XTypedObject
    {
        public XSharedSolutionSpec(
            Initializer initializer,
            ArtifactCenter artifactCenter,
            FileSystem fs,
            SharedSolutionSpec previous = null )
            : base( initializer )
        {
            if( previous != null )
            {
                SharedSpec = new SharedSolutionSpec( previous, artifactCenter, initializer.Element );
                initializer.Services.Remove<SharedSolutionSpec>();
            }
            else
            {
                SharedSpec = new SharedSolutionSpec( initializer.Element, artifactCenter );
            }
            initializer.Services.Add( SharedSpec );
        }

        public SharedSolutionSpec SharedSpec { get; }

    }
}
