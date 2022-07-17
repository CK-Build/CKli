using Microsoft.Extensions.FileProviders;

namespace CK.Env
{
    public interface ITextFileInfo : IFileInfo
    {
        string TextContent { get; }
    }
}
