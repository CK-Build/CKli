using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using CK.Env.MSBuild;
using CK.Env.Analysis;
using CK.Text;
using System.Linq;

namespace CKli
{

    public class XSecondarySolution : XSolutionBase
    {
        readonly Solution _solution;

        public XSecondarySolution(
            Initializer initializer,
            XPrimarySolution primary,
            XPathItem parentFolder,
            XSolutionCentral central )
            : base( initializer,
                    primary.GitBranch,
                    central,
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

        public override Solution GetSolution( IActivityMonitor m, bool reload, string projectToBranchName = null )
        {
            var primary = PrimarySolution.GetSolution( m, false, projectToBranchName );
            return SolutionCentral.MSBuildContext.FindOrLoadSolution( m, GetSolutionFilePath( projectToBranchName ), primary, SpecialType, reload ).Solution;
        }
    }
}
