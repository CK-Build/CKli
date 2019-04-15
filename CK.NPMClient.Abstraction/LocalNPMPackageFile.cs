using CK.Env;
using CSemVer;
using System.IO;
using System.Text.RegularExpressions;

namespace CK.NPMClient
{
    /// <summary>
    /// Simple local package description.
    /// </summary>
    public class LocalNPMPackageFile
    {
        public LocalNPMPackageFile( string fullPath, string id, SVersion v )
        {
            FullPath = fullPath;
            Instance = new ArtifactInstance( "NPM", id, v );
        }

        /// <summary>
        /// Gets the local package full path.
        /// </summary>
        public string FullPath { get; }

        /// <summary>
        /// Gets the package name.
        /// </summary>
        public string PackageId => Instance.Artifact.Name;

        /// <summary>
        /// Gets the package version.
        /// </summary>
        public SVersion Version => Instance.Version;

        /// <summary>
        /// Gets the artifact instance.
        /// </summary>
        public ArtifactInstance Instance { get; }

        /// <summary>
        /// Returns the "<see cref="PackageId"/>.<see cref="Version"/>" string.
        /// </summary>
        /// <returns>The package and version.</returns>
        public override string ToString() => $"{PackageId}.{Version}";

        /// <summary>
        /// Parses a full path and extracts a <see cref="LocalNPMPackageFile"/>.
        /// </summary>
        /// <param name="fullPath">The full path of the package.</param>
        /// <returns>The local NPM package file.</returns>
        public static LocalNPMPackageFile Parse( string fullPath )
        {
            var fName = Path.GetFileNameWithoutExtension( fullPath );
            int idxV = Regex.Match( fName, "\\.\\d" ).Index;
            var id = fName.Substring( 0, idxV );
            var v = SVersion.Parse( fName.Substring( idxV + 1 ) );
            return new LocalNPMPackageFile( fullPath, id, v );
        }
    }

}
