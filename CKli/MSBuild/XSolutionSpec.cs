using CK.Core;
using CK.Env;
using System;
using System.Linq;

namespace CKli
{
    /// <summary>
    /// </summary>
    public class XSolutionSpec : XTypedObject
    {
        public XSolutionSpec(
            Initializer initializer,
            XBranch branch,
            SharedSolutionSpec sharedSpec )
            : base( initializer )
        {
            if( !(initializer.Parent is XBranch) ) throw new Exception( "A SolutionSpec must be a direct child of a Git branch." );

            // The XSolutionSpec is available to siblings (ie. up to the end of the parent git branch).
            initializer.Services.Add( this );
            SolutionSpec = new SolutionSpec( sharedSpec, initializer.Reader );

            XSharedSolutionSpec.RemoveElementWarnings( initializer );
            initializer.Reader.Handle( initializer
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
