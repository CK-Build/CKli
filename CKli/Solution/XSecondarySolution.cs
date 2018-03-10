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
        XPrimarySolution _primarySolution;

        public XSecondarySolution(
            Initializer initializer,
            XBranch branch,
            XPathItem parentFolder,
            XSolutionCentral central )
            : base( initializer,
                    branch,
                    central,
                    parentFolder.FullPath.Combine( (string)initializer.Element.Attribute( "Name" )
                                                    ?? (string)initializer.Element.AttributeRequired( "Path" ) ) )
        {
            initializer.ChildServices.Add( this );
        }

        /// <summary>
        /// Gets the <see cref="XPrimarySolution"/>. This should not be null.
        /// </summary>
        public XPrimarySolution PrimarySolution
        {
            get
            {
                if( _primarySolution == null )
                {
                    _primarySolution = GitBranch.Children.OfType<XPrimarySolution>().FirstOrDefault();
                }
                return _primarySolution;
            }
        }

        protected override void Reset( IRunContext ctx )
        {
            _primarySolution = null;
            base.Reset( ctx );
        }
    }
}
