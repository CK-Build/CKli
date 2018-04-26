using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using CK.Env.MSBuild;
using CK.Env.Analysis;
using CK.Text;

namespace CKli
{
    /// <summary>
    /// A primary solution is the one at the root of the repository whose name
    /// must be the same as the working directory.
    /// It is available for the next siblings.
    /// </summary>
    public class XPrimarySolution : XSolutionBase
    {
        Solution _solution;

        public XPrimarySolution(
            Initializer initializer,
            XBranch branch,
            XSolutionCentral central )
            : base( initializer,
                    branch,
                    central,
                    branch.Parent.Name + ".sln" )
        {
            if( !(initializer.Parent is XBranch) ) throw new Exception( "A primary solution must be a direct child of a Git branch." );
            initializer.Services.Add( this );
        }

        /// <summary>
        /// Gets the <see cref="XBranch"/> that is the direct parent.
        /// </summary>
        public new XBranch Parent => (XBranch)base.Parent;

        /// <summary>
        /// Gets whether <see cref="CK.Env.MSBuild.Solution.TestProjects"/> must be added to <see cref="CK.Env.MSBuild.Solution.PublishedProjects"/>.
        /// </summary>
        public bool TestProjectsArePublished { get; private set; }

        public override Solution GetSolution( IActivityMonitor m, bool reload, string projectToBranchName = null )
        {
            var (s, loaded) = SolutionCentral.MSBuildContext.FindOrLoadSolution( m, GetSolutionFilePath( projectToBranchName ), null, SolutionSpecialType.None, reload );
            if( loaded && TestProjectsArePublished )
            {
                s.PublishedProjects.AddRange( s.TestProjects );
            }
            return s;
        }

    }
}
