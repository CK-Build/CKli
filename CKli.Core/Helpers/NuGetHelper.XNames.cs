using System.Xml.Linq;

namespace CKli.Core;

public static partial class NuGetHelper
{
    public static class XNames
    {
        public static readonly XName Configuration = XNamespace.None + "configuration";
        public static readonly XName PackageSources = XNamespace.None + "packageSources";
        public static readonly XName PackageSourceMapping = XNamespace.None + "packageSourceMapping";
        public static readonly XName Add = XNamespace.None + "add";
        public static readonly XName Key = XNamespace.None + "key";
        public static readonly XName Value = XNamespace.None + "value";
        public static readonly XName PackageSource = XNamespace.None + "packageSource";
        public static readonly XName Package = XNamespace.None + "package";
        public static readonly XName Pattern = XNamespace.None + "pattern";
        public static readonly XName Clear = XNamespace.None + "clear";
        public static readonly XName PackageSourceCredentials = XNamespace.None + "packageSourceCredentials";
    }

}
