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
    public class XRemoveXmlHeaderFromCSProjAction : XAction
    {
        readonly FileSystem _fs;

        public XRemoveXmlHeaderFromCSProjAction(
            Initializer initializer,
            FileSystem fs,
            ActionCollector collector )
            : base( initializer, collector )
        {
            _fs = fs;
        }

        public override bool Run( IActivityMonitor monitor )
        {
            var files = System.IO.Directory.EnumerateFiles( _fs.Root, "*.csproj", System.IO.SearchOption.AllDirectories )
                        .Concat( System.IO.Directory.EnumerateFiles( _fs.Root, "*.proj", System.IO.SearchOption.AllDirectories ) )
                        .Concat( System.IO.Directory.EnumerateFiles( _fs.Root, "*.props", System.IO.SearchOption.AllDirectories ) )
                            .Select( f => (Content: System.IO.File.ReadAllText( f ), Path: f) );
            foreach( var f in files )
            {
                //Removing:
                // <?xml version="1.0" encoding="utf-8"?>
                //    and xmlns
                // <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                
                var c = Regex.Replace( f.Content,
                                       @"\s*<\?.*?\?>\r?\n?", String.Empty,
                                       RegexOptions.CultureInvariant | RegexOptions.IgnoreCase );
                c = Regex.Replace( c,
                                       @"xmlns="".*?""", String.Empty,
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
