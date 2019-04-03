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
        INPMProjectSpec _spec;
        NPMProjectStatus _status;

        internal NPMProject( NPMProjectContext c, IActivityMonitor m, INPMProjectSpec spec )
        {
            NPMContext = c;
            _spec = spec;
            _packageFile = new PackageJsonFile( this );
            _status = RefreshStatus( m );
        }

        public NPMProjectContext NPMContext { get; }

        public FileSystem FileSystem => NPMContext.FileSystem;

        public INPMProjectSpec Specification => _spec;

        public NPMProjectStatus Status => _status;

        public NormalizedPath FullPath => _spec.FullPath;

        public PackageJsonFile PackageJson => _packageFile;

        NPMProjectStatus RefreshStatus( IActivityMonitor m )
        {
            NPMProjectStatus Error( NPMProjectStatus s, string msg = null )
            {
                m.Error( msg ?? $"Error: {s}" );
                return s;
            }

            try
            {
                if( _packageFile.Root == null )
                {
                    return FileSystem.GetDirectoryContents( FullPath ).Exists
                        ? Error( NPMProjectStatus.ErrorMissingPackageJson )
                        : Error( NPMProjectStatus.FatalInitializationError );
                }
                if( _spec.IsPrivate )
                {
                    if( !_packageFile.IsPrivate ) return Error( NPMProjectStatus.ErrorPackageMustBePrivate );
                }
                else
                {
                    if( _packageFile.IsPrivate ) return Error( NPMProjectStatus.ErrorPackageMustNotBePrivate );
                    if( _packageFile.Name == null ) return Error( NPMProjectStatus.ErrorPackageNameMissing );
                    if( _packageFile.Name != _spec.PackageName )
                        return Error( NPMProjectStatus.ErrorPackageInvalidName, $"Expected package name is '{_spec.PackageName}' but found '{_packageFile.Name}'." );
                }               
                return _packageFile.Refresh( m );
            }
            catch( Exception ex )
            {
                m.Error( $"While reading NPM project '{_spec.FullPath}'.", ex );
                return NPMProjectStatus.FatalInitializationError;
            }

        }
    }
}
