using CK.Core;
using CK.Text;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    public class NPMProject
    {
        readonly PackageJsonFile _packageFile;
        INPMProjectDescription _desc;
        NPMProjectStatus _status;

        public NPMProject( NPMContext c, IActivityMonitor m, INPMProjectDescription description )
        {
            NPMContext = c;
            FullPath = description.FullPath;
            _packageFile = new PackageJsonFile( this );
            UpdateDescription( m, description );
        }

        public NPMContext NPMContext { get; }

        public FileSystem FileSystem => NPMContext.FileSystem;

        public INPMProjectDescription Description => _desc;

        public NPMProjectStatus Status => _status;

        public NormalizedPath FullPath { get; }

        public NormalizedPath PackageJsonPath { get; }

        internal void UpdateDescription( IActivityMonitor m, INPMProjectDescription d )
        {
            if( _desc != d )
            {
                _desc = d;
                _status = RefreshStatus( m );
            }
        }

        NPMProjectStatus RefreshStatus( IActivityMonitor m )
        {
            var dir = FileSystem.GetDirectoryContents( FullPath );
            if( !dir.Exists ) return NPMProjectStatus.WarnMissingFolder;
            if( _packageFile.Root == null ) return NPMProjectStatus.WarnMissingPackageJson;

            return NPMProjectStatus.Valid;
        }
    }
}
