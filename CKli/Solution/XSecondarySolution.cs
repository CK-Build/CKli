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
                    parentFolder.FullPath.Combine( (string)initializer.Element.Attribute( "Name" )
                                                    ?? (string)initializer.Element.AttributeRequired( "Path" ) ) )
        {
            PrimarySolution = primary;
            initializer.ChildServices.Add( this );
        }

        /// <summary>
        /// Gets the required <see cref="XPrimarySolution"/>.
        /// </summary>
        public XPrimarySolution PrimarySolution { get; }

        public override SolutionFile ReadSolutionFile( IActivityMonitor m, bool force = false )
        {
            return DoReadSolutionFile( m, PrimarySolution, force );
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
