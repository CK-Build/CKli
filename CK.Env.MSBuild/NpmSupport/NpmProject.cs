using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// NPM project is a simple folder that must contain a package.json file.
    /// </summary>
    public class NpmProject
    {
        readonly NpmProjectDescription _desc;

        NpmProject( Solution s, NormalizedPath fullPath, NormalizedPath packageJson, NpmProjectDescription desc )
        {
            Solution = s;
            _desc = desc;
            PackageJsonPath = packageJson;
            FullPath = fullPath;
        }

        static internal NpmProject Load( IActivityMonitor m, Solution s, NpmProjectDescription desc )
        {
            var fullPath = s.SolutionFolderPath.Combine( desc.Folder );
            var dir = s.GitFolder.FileSystem.GetDirectoryContents( fullPath );
            if( !dir.Exists )
            {
                m.Error( $"Folder not found for expected NPM project '{fullPath}'." );
                return null;
            }
            var packagePath = fullPath.AppendPart( "package.json" );
            var package = s.GitFolder.FileSystem.GetFileInfo( packagePath );
            if( !package.Exists )
            {
                m.Error( $"Unable to find package.json file for expected NPM package '{fullPath}'." );
                return null;
            }
            //TODO: check that desc.IsPrivate == isPrivate in json.

            return new NpmProject( s, fullPath, packagePath, desc );
        }

        /// <summary>
        /// Gets or sets whether the package is private (ie. not published).
        /// Defaults to false.
        /// </summary>
        public bool IsPrivate => _desc.IsPrivate;

        /// <summary>
        /// Gets the solution to which this <see cref="NpmProject"/> belongs.
        /// </summary>
        public Solution Solution { get; }

        /// <summary>
        /// Gets the path to the project folder relative to the <see cref="Solution"/>.
        /// </summary>
        public NormalizedPath SubPath => _desc.Folder;

        /// <summary>
        /// Gets the full path (relative to the <see cref="FileSystem"/>) to the package.json file.
        /// </summary>
        public NormalizedPath PackageJsonPath { get; }

        /// <summary>
        /// Gets the path to the project folder relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath FullPath { get; }
    }
}
