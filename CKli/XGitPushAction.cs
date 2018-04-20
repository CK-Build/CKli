using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CK.Core;
using CK.Env.Analysis;

namespace CKli
{
    public class XGitPushAction : XAction
    {
        public XGitPushAction( Initializer initializer, ActionCollector collector )
            : base( initializer, collector )
        {
        }

        public override bool Run( IActivityMonitor m )
        {
            var gitFolders = NextSiblings.SelectMany( s => s.Descendants<XGitFolder>() );
            foreach( var git in gitFolders )
            {
                if( !git.GitFolder.Push( m ) ) return false;
            }
            return true;
        }
    }
}
