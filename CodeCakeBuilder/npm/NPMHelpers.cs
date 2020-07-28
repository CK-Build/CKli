using CSemVer;
using System.IO;
using System.Text.RegularExpressions;

namespace CodeCakeBuilder.npm
{
    public static class NPMHelpers
    {
        public static string GetPackageTarballFilename( string packageName, SVersion version )
            => packageName.Replace( "@", "" ).Replace( '/', '-' ) + "-" + version.WithBuildMetaData( "" ).ToNormalizedString() + ".tgz";

        /// <summary>
        /// Parses a full path and extracts a <see cref="SVersion"/>.
        /// </summary>
        /// <param name="fullPath">The full path of the package.</param>
        /// <returns>The <see cref="SVersion"/> of the package.</returns>
        public static SVersion GetVersionFromTarballPath( string fullPath )
        {
            var fName = Path.GetFileNameWithoutExtension( fullPath );
            int idxV = Regex.Match( fName, "\\.\\d" ).Index;
            return SVersion.Parse( fName.Substring( idxV + 1 ) );
        }
    }
}
