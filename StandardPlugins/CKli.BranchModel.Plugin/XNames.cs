using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Reusable pre allocated <see cref="System.Xml.Linq.XName"/>.
/// </summary>
public static class XNames
{
#pragma warning disable 1591 //Missing XML comment for publicly visible type or member
    public static readonly XName Explo = XNamespace.None + "Explo";
    public static readonly XName MainLine = XNamespace.None + "MainLine";
    public static readonly XName Parent = XNamespace.None + "Parent";
    public static readonly XName Link = XNamespace.None + "Link";
    public static readonly XName AutoFixUselessBranch = XNamespace.None + "AutoFixUselessBranch";
    public static XName Name => CKli.Core.XNames.Name;
}
