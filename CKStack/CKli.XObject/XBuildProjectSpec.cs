using CK.Core;
using CK.Env;

namespace CKli
{

    /// <summary>
    /// Required Build project specification.
    /// The <see cref="BuildProjectSpec"/> instance is injected in the <see cref="FileSystem.ServiceContainer"/>:
    /// all plugins can use it.
    /// </summary>
    public class XBuildProjectSpec : XTypedObject
    {
        public XBuildProjectSpec( Initializer initializer,
                                  FileSystem fs )
            : base( initializer )
        {
            Spec = new BuildProjectSpec( initializer.Reader );
            initializer.Services.Add( Spec );
            fs.ServiceContainer.Add( Spec );
        }

        public BuildProjectSpec Spec { get; }

    }
}
