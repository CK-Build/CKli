using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using Microsoft.Extensions.FileProviders;

namespace CKli
{
    public class XReplaceBySolutionName : XTypedObject
    {
        XGitFolder _gitFolder;

        public XReplaceBySolutionName(
            Initializer initializer,
            XGitFolder gitFolder,
            IFileInfoHandler target )
            : base( initializer )
        {
            _gitFolder = gitFolder;
            target.AddProcessor( Process );
        }

        private IFileInfo Process( IActivityMonitor m, IFileInfo f )
        {
            var t = f.AsTextFileInfo();
            if( t != null )
            {
                var replaced = t.TextContent.Replace( Text, _gitFolder.Name );
                if( !ReferenceEquals( replaced, t.TextContent ) )
                {
                    f = t.WithText( replaced );
                }
            }
            return f;
        }

        public string Text { get; }

    }
}
