using CK.Core;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace CKli
{
    public interface IFileInfoHandler
    {
        void AddProcessor( Func<IActivityMonitor, IFileInfo, IFileInfo> processor );
    }
}
