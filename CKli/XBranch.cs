using CK.Env;
using System;
using System.Collections.Generic;
using CK.Core;
using CK.Env.MSBuild;
using System.Diagnostics;
using CK.Text;
using CK.Env.Plugins;

namespace CKli
{
    /// <summary>
    /// Defines a Git branch.
    /// A branch usually contains a <see cref="XPrimarySolution"/> and optionals <see cref="XSecondarySolution"/>
    /// and is a <see cref="IBranchSolutionLoader"/> that is injected into the <see cref="IGitPluginCollection{T}.ServiceContainer"/>
    /// of the branch so that <see cref="IGitBranchPlugin"/> can load solutions.
    /// </summary>
    public class XBranch : XPathItem, IBranchSolutionLoader
    {
        readonly List<XSolutionBase> _solutions;
        NormalizedPath[] _solutionFilePaths;

        public XBranch(
            Initializer initializer,
            XGitFolder parent )
            : base( initializer, parent.FileSystem, parent.FullPath.AppendPart( "branches" ) )
        {
            _solutions = new List<XSolutionBase>();
            initializer.ChildServices.Add( this );
            parent.Register( this );
            parent.GitFolder.PluginManager.GetServiceContainer( Name ).Add<IBranchSolutionLoader>( this );
        }

        /// <summary>
        /// Gets the parent <see cref="XGitFolder"/> object.
        /// </summary>
        public new XGitFolder Parent => (XGitFolder)base.Parent;

        /// <summary>
        /// Gets the primary solution if one is defined in this branch.
        /// </summary>
        public XPrimarySolution PrimarySolution { get; private set; }

        /// <summary>
        /// Gets all the solutions regardless of their type in this branch in the order of
        /// the World xml file.
        /// </summary>
        public IReadOnlyList<XSolutionBase> Solutions => _solutions;

        internal void Register( XSolutionBase s )
        {
            _solutions.Add( s );
            if( s is XPrimarySolution p )
            {
                if( PrimarySolution != null ) throw new InvalidOperationException( "Only one XPrimarySolution allowed in a branch." );
                PrimarySolution = p;
            }
        }

        NormalizedPath IBranchSolutionLoader.BranchPathDefiner => FullPath;

        /// <summary>
        /// Obtains the primary solution from this branch (or from another branch) with all its existing secondary solutions
        /// available in <see cref="Solution.LoadedSecondarySolutions"/>
        /// This is null if no primary solution is defined.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to reload the solution.</param>
        /// <param name="projectToBranchName">Optional other branch for which the solution must be loaded.</param>
        /// <returns>The primary solution or null.</returns>
        public Solution GetPrimarySolution( IActivityMonitor m, bool reload, string projectToBranchName = null )
        {
            if( PrimarySolution == null ) return null;
            Debug.Assert( _solutions[0] == PrimarySolution );
            var p = PrimarySolution.GetSolution( m, reload, projectToBranchName );
            for( int i = 1; i < _solutions.Count; ++i )
            {
                _solutions[i].GetSolution( m, reload, projectToBranchName );
            }
            return p;
        }

    }
}
