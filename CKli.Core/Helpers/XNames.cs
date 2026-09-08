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
    public static readonly XName References = XNamespace.None + "References";
    public static readonly XName Reference = XNamespace.None + "Reference";
    public static readonly XName DefaultClone = XNamespace.None + "DefaultClone";
    public static readonly XName Private = XNamespace.None + "Private";
    public static readonly XName LTSName = XNamespace.None + "LTSName";
    public static readonly XName Folder = XNamespace.None + "Folder";
    public static readonly XName Name = XNamespace.None + "Name";
    public static readonly XName Url = XNamespace.None + "Url";
    public static readonly XName Version = XNamespace.None + "Version";
    public static readonly XName PackageVersion = XNamespace.None + "PackageVersion";
    public static readonly XName ItemGroup = XNamespace.None + "ItemGroup";
    public static readonly XName PackageReference = XNamespace.None + "PackageReference";
    public static readonly XName ProjectReference = XNamespace.None + "ProjectReference";
    public static readonly XName Include = XNamespace.None + "Include";
}
