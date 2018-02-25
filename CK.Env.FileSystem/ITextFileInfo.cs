using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public interface ITextFileInfo : IFileInfo
    {
        string TextContent { get; }
    }
}
