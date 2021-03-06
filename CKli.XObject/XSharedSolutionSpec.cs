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
        public XSharedSolutionSpec(
            Initializer initializer,
            SharedSolutionSpec previous = null )
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
            //RemoveElementWarnings( initializer );
            initializer.Services.Add( SharedSpec );
        }

        //static internal void RemoveElementWarnings( Initializer initializer )
        //{
        //    initializer.Reader.Handle( initializer
        //                                .Element.Elements()
        //                                .Where( c => c.Name.LocalName == nameof( SharedSolutionSpec.NuGetSources )
        //                                                || c.Name.LocalName == nameof( SharedSolutionSpec.RemoveNuGetSourceNames )
        //                                                || c.Name.LocalName == nameof( SharedSolutionSpec.NPMSources )
        //                                                || c.Name.LocalName == nameof( SharedSolutionSpec.RemoveNPMScopeNames )
        //                                                || c.Name.LocalName == nameof( SharedSolutionSpec.ArtifactTargets )
        //                                                || c.Name.LocalName == nameof( SharedSolutionSpec.ExcludedPlugins ) ) );
        //}

        public SharedSolutionSpec SharedSpec { get; }

    }
}
