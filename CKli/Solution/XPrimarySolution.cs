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
        /// Gets all the secondary solutions of the <see cref="Parent"/> branch.
        /// </summary>
        public IEnumerable<XSecondarySolution> SecondarySolutions => Parent.Descendants<XSecondarySolution>();

    }
}
