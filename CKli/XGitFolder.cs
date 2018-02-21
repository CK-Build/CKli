using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;

namespace CKli
{
    public class XGitFolder : XPathItem
    {
        public XGitFolder(
            Initializer initializer,
            XPathItem parent)
            : base(initializer, parent.FileSystem, parent)
        {
            initializer.ChildServices.Add( this );
            FileSystem.EnsureGitFolder( FullPath );
        }

    }
}
