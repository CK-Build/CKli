using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

public static class XNames
{
#pragma warning disable 1591 //Missing XML comment for publicly visible type or member
    public static readonly XName VersionOverride = XNamespace.None + "VersionOverride";
    public static readonly XName Path = XNamespace.None + "Path";
    public static readonly XName Project = XNamespace.None + "Project";
    public static readonly XName PropertyGroup = XNamespace.None + "PropertyGroup";
    public static readonly XName IsPackable = XNamespace.None + "IsPackable";
    public static XName Version => CKli.Core.XNames.Version;
    public static XName PackageVersion => CKli.Core.XNames.PackageVersion;
    public static XName PackageReference => CKli.Core.XNames.PackageReference;
    public static XName Include => CKli.Core.XNames.Include;
}
