using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using System.Linq;

namespace CKli
{
    /// <summary>
    /// Secondary solutions requires a <see cref="XPrimarySolution"/> before.
    /// This is available below (children) but not after it.
    /// </summary>
    public class XSecondarySolution : XSolutionBase
    {
        public XSecondarySolution(
            Initializer initializer,
            XPrimarySolution primary,
            XPathItem parentFolder,
            XSolutionCentral central )
            : base( initializer,
                    primary.GitBranch,
                    central,
                    primary.SolutionSettings,
                    (string)initializer.Element.Attribute( "Name" ) ?? (string)initializer.Element.AttributeRequired( "Path" ) )
        {
            PrimarySolution = primary;
            initializer.ChildServices.Add( this );
            if( SpecialType == SolutionSpecialType.None ) SpecialType = SolutionSpecialType.IncludedSecondarySolution;
        }

        /// <summary>
        /// Gets the required <see cref="XPrimarySolution"/>.
        /// </summary>
        public XPrimarySolution PrimarySolution { get; }

        /// <summary>
        /// Gets the optional <see cref="SpecialType"/> indicator.
        /// </summary>
        public SolutionSpecialType SpecialType { get; private set; }

        /// <summary>
        /// Overridden to load a secondary solution with the current <see cref="XSolutionSettings.SolutionSettings"/>
        /// bound to the <see cref="PrimarySolution"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to reload the solution.</param>
        /// <param name="projectToBranchName">Optional other branch for which the solution must be loaded.</param>
        /// <returns>The secondary solution or null if not found.</returns>
        public override Solution GetSolution( IActivityMonitor m, bool reload, string projectToBranchName = null )
        {
            var primary = PrimarySolution.GetSolution( m, false, projectToBranchName );
            var r = SolutionCentral.MSBuildContext.FindOrLoadSolution(
                        m,
                        GetSolutionFilePath( projectToBranchName ),
                        primary,
                        SpecialType,
                        reload );
            if( r.Loaded )
            {
                HandleArtifactTargetNames( m, r.Solution );
            }
            return r.Solution;
        }
    }
}
