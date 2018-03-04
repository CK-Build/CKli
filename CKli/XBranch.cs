using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;

namespace CKli
{
    public class XBranch : XPathItem
    {
        public XBranch(
            Initializer initializer,
            XGitFolder parent )
            : base( initializer, parent.FileSystem, parent.FullPath.AppendPart( "branches" ) )
        {
            initializer.ChildServices.Add( this );
        }

        public new XGitFolder Parent => (XGitFolder)base.Parent;

    }
}
