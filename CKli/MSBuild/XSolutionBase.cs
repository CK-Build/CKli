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
    public abstract class XSolutionBase : XPathItem
    {
        readonly XBranch _branch;

        protected XSolutionBase(
            Initializer initializer,
            XBranch branch,
            XSolutionCentral central,
            XSolutionSettings solutionSettings,
            NormalizedPath branchBasedSolutionFilePath )
            : base( initializer,
                    branch.FileSystem,
                    FileSystemItemKind.File,
                    branch.FullPath.Combine( branchBasedSolutionFilePath ) )
        {
            _branch = branch;
            SolutionSettings = solutionSettings;
            initializer.ChildServices.Add( this );
            branch.Register( this );
        }

        public XSolutionCentral SolutionCentral => GitBranch.Parent.SolutionCentral;

        public XBranch GitBranch => _branch;

        /// <summary>
        /// Gets the current solution settings that applies to this solution.
        /// </summary>
        public XSolutionSettings SolutionSettings { get; }

        /// <summary>
        /// Gets the path of this solution for this branch or another branch.
        /// </summary>
        /// <param name="projectToBranchName">Optional other branch for which path must be obtained.</param>
        /// <returns>The path of this solution file in the specified branch.</returns>
        public NormalizedPath GetSolutionFilePath( string projectToBranchName = null )
        {
            var path = FullPath;
            if( !String.IsNullOrWhiteSpace( projectToBranchName ) && projectToBranchName != GitBranch.Name )
            {
                path = GitBranch.FullPath
                                     .RemoveLastPart()
                                     .AppendPart( projectToBranchName )
                                     .Combine( FullPath.RemovePrefix( GitBranch.FullPath ) );
            }
            return path;
        }

        /// <summary>
        /// Obtains a <see cref="Solution"/> already loaded or loads it from this branch or from another branch.
        /// The two specializations <see cref="XPrimarySolution.GetSolution"/> and <see cref="XSecondarySolution.GetSolution"/>
        /// adapts the loading process.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to reload the solution.</param>
        /// <param name="projectToBranchName">Optional other branch for which the solution must be loaded.</param>
        /// <returns>The solution or null if not found.</returns>
        public abstract Solution GetSolution( IActivityMonitor m, bool reload, string projectToBranchName = null );
    }
}

