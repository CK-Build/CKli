using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CKli
{
    public class XLaunchSettingsUpdateNugetExePathAction : XAction
    {
        readonly FileSystem _fs;

        public XLaunchSettingsUpdateNugetExePathAction(
            Initializer initializer,
            FileSystem fs,
            ActionCollector collector )
            : base( initializer, collector )
        {
            _fs = fs;
        }

        public override bool Run( IActivityMonitor monitor )
        {
            var files = System.IO.Directory.EnumerateFiles( _fs.Root, "launchSettings.json", System.IO.SearchOption.AllDirectories )
                            .Select( f => (Content: System.IO.File.ReadAllText( f ), Path: f) );
            foreach( var f in files )
            {
                var c = Regex.Replace( f.Content,
                                       @"executablePath"":\s*"".*\\nunit\.exe"",",
                                       @"executablePath"": ""%USERPROFILE%\\.nuget\\packages\\nunit.runners.net4\\2.6.4\\tools\\nunit.exe"",",
                                       RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );
                if( c != f.Content )
                {
                    monitor.Info( $"Updated '{f.Path}'." );
                    System.IO.File.WriteAllText( f.Path, c );
                }
            }
            return true;
        }
    }
}
