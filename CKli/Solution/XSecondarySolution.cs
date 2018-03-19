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
        }

        /// <summary>
        /// Gets the required <see cref="XPrimarySolution"/>.
        /// </summary>
        public XPrimarySolution PrimarySolution { get; }

        /// <summary>
        /// Gets the optional <see cref="SpecialType"/> indicator.
        /// </summary>
        public SolutionFileSpecialType SpecialType { get; private set; }

        public override SolutionFile ReadSolutionFile( IActivityMonitor m, bool force = false )
        {
            var s = DoReadSolutionFile( m, PrimarySolution, force );
            s.SpecialType = SpecialType;
            return s;
        }

        internal void OnPrimarySolutionFileChanged( SolutionFile newPrimary )
        {
            if( _solution != null )
            {
                _solution.PrimarySolution = newPrimary;
            }
        }

    }
}
