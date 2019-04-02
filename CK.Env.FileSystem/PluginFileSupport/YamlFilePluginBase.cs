using CK.Core;
using CK.Env.Plugins;
using CK.Text;
using SharpYaml;
using SharpYaml.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.Plugins
{
    public class YamlFilePluginBase : YamlFileBase
    {
        readonly GitBranchPluginImpl _pluginImpl;

        public YamlFilePluginBase( GitFolder f, NormalizedPath branchPath, NormalizedPath filePath )
            : base( f.FileSystem, filePath )
        {
            if( !filePath.StartsWith( branchPath ) ) throw new ArgumentException( $"Path {filePath} must start with folder {f.SubPath}." );
            _pluginImpl = new GitBranchPluginImpl( f, branchPath );
        }

    }
}
