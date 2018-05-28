using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    public struct VersionedPackage
    {
        public readonly string PackageId;
        public readonly SVersion Version;

        public VersionedPackage( string p, SVersion v )
        {
            PackageId = p;
            Version = v;
        }
    }
}
