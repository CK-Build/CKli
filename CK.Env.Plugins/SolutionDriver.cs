using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Env.MSBuild;
using CK.Text;

namespace CK.Env.Plugins
{
    public class SolutionDriver : IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly IBranchSolutionLoader _solutionLoader;
        Solution _solution;

        public SolutionDriver( GitFolder f, NormalizedPath branchPath, IBranchSolutionLoader solutionLoader )
        {
            BranchPath = branchPath;
            Folder = f;
            _solutionLoader = solutionLoader;
        }

        public NormalizedPath BranchPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( "SolutionDriver" );

        public GitFolder Folder { get; }

        /// <summary>
        /// Loads or reloads the <see cref="PrimarySolution"/> and its secondary solutions.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to force the reload of the solutions.</param>
        /// <returns>The primary solution or null.</returns>
        public Solution EnsureLoaded( IActivityMonitor m, bool reload = false )
        {
            if( _solution == null )
            {
                _solution = _solutionLoader.GetPrimarySolution( m, reload, BranchPath.LastPart );
                if( _solution == null )
                {
                    m.Error( $"Unable to load primary solution '{BranchPath}/{BranchPath.LastPart}.sln'." );
                }
            }
            return _solution;
        }

        /// <summary>
        /// Gets the already loaded (see <see cref="EnsureLoaded(IActivityMonitor, bool)"/>) primary solution
        /// and its secondary solutions.
        /// </summary>
        public Solution PrimarySolution => _solution;


    }
}
