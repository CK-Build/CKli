using CK.Env.Plugins;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    public class PackageJsonFile : JsonFileBase
    {
        internal PackageJsonFile( NPMProject p )
            : base( p.FileSystem, p.FullPath.AppendPart( "package.json" ) )
        {
        }

        public string Name
        {
            get => (string)Root["name"];
            set => Root["name"] = value;
        }

        public bool IsPrivate
        {
            get => (bool?)Root["private"] ?? false;
            set => Root["private"] = value;
        }

        public SVersion Version
        {
            get => SVersion.Parse( (string)Root["version"] );
            set => Root["version"] = value.ToNuGetPackageString();
        }
    }
}
