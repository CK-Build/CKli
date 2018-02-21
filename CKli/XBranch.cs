using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CKli
{
    public class XBranch : XPathItem
    {
        public XBranch(
            Initializer initializer,
            XGitFolder parent )
            : base( initializer, parent.FileSystem, parent.FullPath.AppendPart( "branches" ) )
        {
        }

        public new XGitFolder Parent => (XGitFolder)base.Parent;

    }
}
