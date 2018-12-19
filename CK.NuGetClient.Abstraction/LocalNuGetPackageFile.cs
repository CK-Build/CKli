using CK.Env;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CK.NuGetClient
{
    /// <summary>
    /// Simple local package description.
    /// </summary>
    public class LocalNuGetPackageFile : IArtifactLocator
    {
        public LocalNuGetPackageFile( string fullPath, string id, SVersion v )
        {
            FullPath = fullPath;
            Instance = new ArtifactInstance( "NuGet", id, v );
        }

        public string FullPath { get; }

        public string PackageId => Instance.Artifact.Name;

        public SVersion Version => Instance.Version;

        public ArtifactInstance Instance { get; }

        public static LocalNuGetPackageFile Parse( string fullPath )
        {
            var fName = Path.GetFileNameWithoutExtension( fullPath );
            int idxV = Regex.Match( fName, "\\.\\d" ).Index;
            var id = fName.Substring( 0, idxV );
            var v = SVersion.Parse( fName.Substring( idxV + 1 ) );
            return new LocalNuGetPackageFile( fullPath, id, v );
        }
    }

}
