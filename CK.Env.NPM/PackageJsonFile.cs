using CK.Env.Plugins;
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

    }
}
