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
    public class LocalNuGetPackageFile
    {
        public readonly string FullPath;
        public readonly string PackageId;
        public readonly SVersion Version;
        public readonly bool Symbols;

        public LocalNuGetPackageFile( string fullPath, string id, SVersion v, bool symbols )
        {
            FullPath = fullPath;
            PackageId = id;
            Version = v;
            Symbols = symbols;
        }

        public static LocalNuGetPackageFile Parse( string fullPath )
        {
            var fName = Path.GetFileNameWithoutExtension( fullPath );
            bool symbols = fName.EndsWith( ".symbols" );
            if( symbols ) fName = fName.Substring( 0, fName.Length - 8 );
            int idxV = Regex.Match( fName, "\\.\\d" ).Index;
            var id = fName.Substring( 0, idxV );
            var v = SVersion.Parse( fName.Substring( idxV + 1 ) );
            return new LocalNuGetPackageFile( fullPath, id, v, symbols );
        }
    }

}
