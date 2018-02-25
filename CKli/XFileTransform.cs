using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using Microsoft.Extensions.FileProviders;

namespace CKli
{
    public class XFileTransform : XTypedObject, IFileInfoHandler
    {
        readonly IFileInfoHandler _target;

        public XFileTransform(
            Initializer initializer,
            IFileInfoHandler target )
            : base( initializer )
        {
            _target = target;
        }

        void IFileInfoHandler.AddProcessor( Func<IActivityMonitor, IFileInfo, IFileInfo> p ) => _target.AddProcessor( p );
    }
}
