using CK.Core;
using CK.Env;

namespace CKli
{
    public class XSolutionSettings : XTypedObject
    {
        public XSolutionSettings(
            Initializer initializer,
            XArtifactCenter artifactHandler,
            XSolutionSettings previous = null )
            : base( initializer )
        {
            if( previous != null )
            {
                SolutionSettings = new CommonSolutionSpec( previous.SolutionSettings, artifactHandler.ArtifactCenter, initializer.Element );
                initializer.Services.Remove<XSolutionSettings>();
            }
            else
            {
                SolutionSettings = new CommonSolutionSpec( initializer.Element, artifactHandler.ArtifactCenter );
            }

            initializer.Services.Add( this );
        }

        public ICommonSolutionSpec SolutionSettings { get; }

    }
}
