using CK.Core;
using CK.Env;

namespace CKli
{

    /// <summary>
    /// Required shared solution specification.
    /// The first occurrence injects a <see cref="SharedSolutionSpec"/>, subsequent occurrences
    /// injects a modified clone.
    /// </summary>
    public class XSharedSolutionSpec : XTypedObject
    {
        public XSharedSolutionSpec( Initializer initializer,
                                    SharedSolutionSpec? previous = null )
            : base( initializer )
        {
            if( previous != null )
            {
                SharedSpec = new SharedSolutionSpec( previous, initializer.Reader );
                initializer.Services.Remove<SharedSolutionSpec>();
            }
            else
            {
                SharedSpec = new SharedSolutionSpec( initializer.Reader );
            }
            initializer.Services.Add( SharedSpec );
        }

        public SharedSolutionSpec SharedSpec { get; }

    }
}
