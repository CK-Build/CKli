using System.Xml.Linq;

namespace CKli.Build.Plugin;

/// <summary>
/// Reusable pre allocated <see cref="System.Xml.Linq.XName"/>.
/// </summary>
public static class XNames
{
#pragma warning disable 1591 //Missing XML comment for publicly visible type or member
    public static readonly XName DeleteBeforeBuild = XNamespace.None + "DeleteBeforeBuild";
}
