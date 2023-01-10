using CK.Core;
using CK.Env.NodeSln;
using CK.Env.NPM;
using CSemVer;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public static class NPMEnvLocalFeedProviderExtension
    {
        internal static IEnumerable<LocalNPMPackageFile> GetAllNPMPackageFiles( IActivityMonitor m, string feedPath )
        {
            return System.IO.Directory.EnumerateFiles( feedPath, "*.tgz" )
                                        .Select( f => LocalNPMPackageFile.Parse( f ) );
        }
    }
}
