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
    class NPMProject : INPMProject
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

        /// <summary>
        /// Gets the project specification.
        /// </summary>
        public INPMProjectSpec Specification => _spec;

        /// <summary>
        /// Gets the project status (that can be on error).
        /// </summary>
        public NPMProjectStatus Status => _status;

        /// <summary>
        /// Gets the project folder path relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath FullPath => _spec.FullPath;

        /// <summary>
        /// Gets the package.json file object.
        /// </summary>
        public PackageJsonFile PackageJson => _packageFile;

        public NPMProjectStatus RefreshStatus( IActivityMonitor m )
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
                    {
                        return Error( NPMProjectStatus.ErrorPackageInvalidName, $"Expected package name is '{_spec.PackageName}' but found '{_packageFile.Name}'." );
                    }
                }
                return _packageFile.Refresh( m );
            }
            catch( Exception ex )
            {
                m.Error( $"While reading NPM project '{_spec.FullPath}'.", ex );
                return NPMProjectStatus.FatalInitializationError;
            }

        }

        public override string ToString() => _packageFile.ToString();
    }
}
