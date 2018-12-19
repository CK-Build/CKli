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

namespace CK.Env.Plugins.SolutionFiles
{
    public class CommonFolder : PluginFolderBase
    {
        readonly ISolutionSettings _settings;

        public CommonFolder( GitFolder f, ISolutionSettings settings, NormalizedPath branchPath )
            : base( f, branchPath, "Common" )
        {
            _settings = settings;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            if( _settings.NoStrongNameSigning )
            {
                DeleteFile( m, "SharedKey.snk" );
            }
            else
            {
                SetBinaryResource( m, "SharedKey.snk" );
            }
        }
    }
}
