using CK.Core;
using CSemVer;

namespace CK.Env.MSBuild
{
    class SVersionRange
    {
        public static (bool Locked, SVersion Version) TryParseSimpleRange(IActivityMonitor m, string r )
        {
            SVersion v = SVersion.TryParse( r );
            if( !v.IsValid
                && r != null && r.Length > 3 )
            {
                if( r[0] == '[' && r[r.Length - 1] == ']' )
                {
                    return (true, SVersion.TryParse( r.Substring( 1, r.Length - 2 ) ));
                }

                string[] numbers = r.Split( '.' );
                if(numbers.Length == 4)
                {
                    m.Warn( "Old version in format X.Y.Z.A removing last number to parse the NuGet Version" );
                    v = SVersion.Create( int.Parse(numbers[0]), int.Parse(numbers[1]), int.Parse(numbers[2]) );
                }

            }
            return (false, v);
        }

    }
}
