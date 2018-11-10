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
    public class XAppveyorFileAction : XAction
    {
        readonly FileSystem _fs;

        public XAppveyorFileAction(
            Initializer initializer,
            FileSystem fs,
            ActionCollector collector )
            : base( initializer, collector )
        {
            _fs = fs;
        }

        public override bool Run( IActivityMonitor monitor )
        {
            var files = System.IO.Directory.EnumerateFiles( _fs.Root, "appveyor.yml", System.IO.SearchOption.AllDirectories );
            foreach( var f in files )
            {
            }
            return true;
        }
    }
}
