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
            RemoveElementWarnings( initializer );
            initializer.Services.Add( SharedSpec );
        }

        static internal void RemoveElementWarnings( Initializer initializer )
        {
            var s = nameof( SharedSolutionSpec.NuGetSources );
            initializer.HandledElements.AddRange( initializer
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
