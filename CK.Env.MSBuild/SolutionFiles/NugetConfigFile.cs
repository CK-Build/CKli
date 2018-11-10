using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class NugetConfigFile : NuGetConfigBaseFile
    {
        readonly Solution _solution;

        public NugetConfigFile( Solution s )
            : base( s.GitFolder )
        {
            _solution = s;
        }

        public bool ApplySettings( IActivityMonitor m )
        {
            if( _solution.Settings.SuppressNuGetConfigFile )
            {
                Delete( m );
                return true;
            }
            EnsureDocument();
            PackageSources.EnsureFirstElement( "clear" );
            foreach( var s in _solution.Settings.NuGetSources )
            {
                EnsureFeed( m, s.Name, s.Url );
                if( s.Credentials != null )
                {
                    EnsureFeedCredentials( m, s.Name, s.Credentials.UserName, s.Credentials.Password );
                }
            }
            foreach( var name in _solution.Settings.ExcludedNuGetSourceNames )
            {
                RemoveFeed( m, name, withCredentials: true );
            }
            return true;
        }


    }
}
