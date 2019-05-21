using CK.Core;
using CK.Env;
using CK.Env.DependencyModel;
using System.Linq;

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
                SharedSpec = new SharedSolutionSpec( previous, initializer );
                initializer.Services.Remove<SharedSolutionSpec>();
            }
            else
            {
                SharedSpec = new SharedSolutionSpec( initializer );
            }
            RemoveElementWarnings( initializer );
            initializer.Services.Add( SharedSpec );
        }

        static internal void RemoveElementWarnings( Initializer initializer )
        {
            initializer.HandledObjects.AddRange( initializer
                                                    .Element.Elements()
                                                    .Where( c => c.Name.LocalName == nameof( SharedSolutionSpec.NuGetSources )
                                                                 || c.Name.LocalName == nameof( SharedSolutionSpec.RemoveNuGetSourceNames )
                                                                 || c.Name.LocalName == nameof( SharedSolutionSpec.NPMSources )
                                                                 || c.Name.LocalName == nameof( SharedSolutionSpec.RemoveNPMScopeNames )
                                                                 || c.Name.LocalName == nameof( SharedSolutionSpec.ArtifactTargets )
                                                                 || c.Name.LocalName == nameof( SharedSolutionSpec.ExcludedPlugins ) ) );
        }

        public SharedSolutionSpec SharedSpec { get; }

    }
}
