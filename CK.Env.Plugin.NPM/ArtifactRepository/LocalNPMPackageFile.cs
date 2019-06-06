using CSemVer;
using System.IO;
using System.Text.RegularExpressions;

namespace CK.Env.NPM
{
    /// <summary>
    /// Simple local package description.
    /// </summary>
    public class LocalNPMPackageFile
    {
        public LocalNPMPackageFile( string fullPath, string id, SVersion v )
        {
            FullPath = fullPath;
            Instance = new ArtifactInstance( NPMClient.NPMType, id, v );
        }

        /// <summary>
        /// Gets the local package full path.
        /// </summary>
        public string FullPath { get; }

        public string PackageScope => Instance.Artifact.Name.Substring( 0, Instance.Artifact.Name.IndexOf( '/' ) );

        /// <summary>
        /// Gets the artifact instance.
        /// </summary>
        public ArtifactInstance Instance { get; }

        /// <summary>
        /// Returns the <see cref="Instance"/> string.
        /// </summary>
        /// <returns>The package and version.</returns>
        public override string ToString() => Instance.ToString();

        /// <summary>
        /// Parses a full path and extracts a <see cref="LocalNPMPackageFile"/>.
        /// </summary>
        /// <param name="fullPath">The full path of the package.</param>
        /// <returns>The local NPM package file.</returns>
        public static LocalNPMPackageFile Parse( string fullPath )
        {
            var fName = Path.GetFileNameWithoutExtension( fullPath );
            int idxV = Regex.Match( fName, "-\\d" ).Index;
            var id = fName.Substring( 0, idxV );
            var v = SVersion.Parse( fName.Substring( idxV + 1 ) );
            return new LocalNPMPackageFile( fullPath, id, v );
        }
    }

}
