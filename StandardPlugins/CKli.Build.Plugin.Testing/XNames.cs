using System.Xml.Linq;

namespace CKli.Build.Plugin.Testing;

public static class XNames
{
#pragma warning disable 1591 //Missing XML comment for publicly visible type or member
    public static XName PackageReference => CKli.Core.XNames.PackageReference;
    public static XName Include => CKli.Core.XNames.Include;
}
