using CK.Core;
using CK.Env;
using System;
using System.Linq;

namespace CKli
{
    /// <summary>
    /// A primary solution is the one at the root of the repository whose name
    /// must be the same as the working directory. It is available for the next siblings.
    /// </summary>
    public class XSolutionSpec : XPathItem
    {
        public XSolutionSpec(
            Initializer initializer,
            XBranch branch,
            SharedSolutionSpec sharedSpec,
            ArtifactCenter artifactCenter )
            : base( initializer,
                    branch.FileSystem,
                    FileSystemItemKind.File,
                    branch.FullPath.Combine( branch.Parent.Name + ".sln" ) )
        {
            if( !(initializer.Parent is XBranch) ) throw new Exception( "A SolutionSpec must be a direct child of a Git branch." );

            // The XSolutionSpec is available to siblings (ie. up to the end of the parent git branch).
            initializer.Services.Add( this );
            SolutionSpec = new SolutionSpec( sharedSpec, artifactCenter, initializer.Element );

            XSharedSolutionSpec.RemoveElementWarnings( initializer );
            initializer.HandledElements.AddRange( initializer
                                                    .Element.Elements()
                                                    .Where( c => c.Name.LocalName == nameof( SolutionSpec.NPMProjects )
                                                                 || c.Name.LocalName == nameof( SolutionSpec.PublishedProjects )
                                                                 || c.Name.LocalName == nameof( SolutionSpec.NotPublishedProjects )
                                                                 || c.Name.LocalName == nameof( SolutionSpec.CKSetupComponentProjects ) ) );

            // Registers the SolutionSpec as a branch settings: the Solution specifications becomes
            // available to any of this branch plugins.
            branch.Parent.GitFolder.PluginManager.RegisterSettings( SolutionSpec, branch.Name );           
            foreach( var type in GitPluginManager.GlobalRegister.GetAllGitPlugins().Except( SolutionSpec.ExcludedPlugins ) )
            {
                branch.Parent.GitFolder.PluginManager.Register( type, branch.Name, allowGitPlugin: true );
            }
        }

        /// <summary>
        /// Gets the <see cref="XBranch"/> that is the direct parent.
        /// </summary>
        public new XBranch Parent => (XBranch)base.Parent;

        /// <summary>
        /// Gets the solution specification.
        /// </summary>
        public SolutionSpec SolutionSpec { get; }
    }
}
