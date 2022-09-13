using CK.Build;
using CSemVer;
using NuGet.Protocol;
using System.IO;
using System.Text.RegularExpressions;

namespace CK.Env.NuGet
{
    public sealed class NugetPackageMetadata
    {
        public PackageSearchMetadata? PackageSearchMetadata { get; set; }
        public string? GitUrl { get; set; }
    }
}
