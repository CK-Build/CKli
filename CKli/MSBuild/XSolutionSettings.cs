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
                SolutionSettings = new SolutionSettings( previous.SolutionSettings, artifactHandler.ArtifactCenter, initializer.Element );
            }
            else SolutionSettings = new SolutionSettings( initializer.Element, artifactHandler.ArtifactCenter );
            initializer.Services.Add( this );
        }

        public ISolutionSettings SolutionSettings { get; }

    }
}
