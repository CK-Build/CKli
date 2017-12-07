using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CKli
{
    public class XGitFolder : XPathItem
    {
        public XGitFolder(
            Initializer initializer,
            XPathItem parent)
            : base(initializer, parent.FileSystem, parent)
        {
            IsFolder = true;
        }

    }
}
