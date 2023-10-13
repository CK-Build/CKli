using CK.Core;
using CK.Env;
using CK.Monitoring;

using NuGet.Protocol.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CKli
{

    partial class Program
    {
        static int Main( string[] args )
        {
            NormalizedPath userHostPath = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
            userHostPath = userHostPath.AppendPart( "CKli" );
            LogFile.RootLogPath = userHostPath.AppendPart( "Logs" );

            var logConfig = new GrandOutputConfiguration().AddHandler(
                                new CK.Monitoring.Handlers.TextFileConfiguration() { Path = "Text", MaxCountPerFile = 200_000 } );
            GrandOutput.EnsureActiveDefault( logConfig );

            var monitor = new ActivityMonitor{ MinimalFilter = LogFilter.Debug };
            try
            {
                if( args.Length > 0 )
                {
                    var appContext = BasicApplicationContext.Create( monitor );
                    if( appContext == null ) return 1;
                    return CkliMain( monitor, appContext, args );
                }
                LegacyInteractive( monitor );
                return 0;
            }
            catch( Exception ex )
            {
                monitor.Fatal( ex );
                return 2;
            }
            finally
            {
                GrandOutput.Default?.Dispose();
            }
        }
    }
}
