using CSemVer;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Feeds
{
    public struct VersionedPackage
    {
        public readonly string PackageName;
        public readonly SVersion Version;

        public VersionedPackage( string n, SVersion s )
        {
            PackageName = n;
            Version = s;
        }
    }
}
