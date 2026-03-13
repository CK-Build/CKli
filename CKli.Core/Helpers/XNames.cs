using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Reusable pre allocated <see cref="System.Xml.Linq.XName"/>.
/// </summary>
public static class XNames
{
    #pragma warning disable 1591 //Missing XML comment for publicly visible type or member

    public static readonly XName Plugins = XNamespace.None + "Plugins";
    public static readonly XName Disabled = XNamespace.None + "Disabled";
    public static readonly XName CompileMode = XNamespace.None + "CompileMode";
    public static readonly XName Repository = XNamespace.None + "Repository";
    public static readonly XName Folder = XNamespace.None + "Folder";
    public static readonly XName Name = XNamespace.None + "Name";
    public static readonly XName Url = XNamespace.None + "Url";
}
