using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class CommonFolder : PluginFolderBase
    {
        public CommonFolder( GitFolder f, NormalizedPath branchPath )
            : base( f, branchPath, "Common" )
        {
        }

        protected override void DoCopyResources( IActivityMonitor m )
        {
            CopyBinaryResource( m, "SharedKey.snk" );
        }
    }
}
