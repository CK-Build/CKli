using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using System.Xml.Linq;

namespace CKli
{
    public class XBranch : XPathItem
    {
        readonly List<XSolutionBase> _solutions;

        public XBranch(
            Initializer initializer,
            XGitFolder parent )
            : base( initializer, parent.FileSystem, parent.FullPath.AppendPart( "branches" ) )
        {
            _solutions = new List<XSolutionBase>();
            initializer.ChildServices.Add( this );
            parent.Register( this );
        }

        public new XGitFolder Parent => (XGitFolder)base.Parent;

        /// <summary>
        /// Gets all the solutions regardless of their type in this branch in the order of
        /// the World xml file.
        /// </summary>
        public IReadOnlyList<XSolutionBase> Solutions => _solutions;

        internal void Register( XSolutionBase s )
        {
            _solutions.Add( s );
        }
    }
}
