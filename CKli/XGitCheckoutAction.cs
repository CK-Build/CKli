using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CK.Core;
using CK.Env.Analysis;

namespace CKli
{
    public class XGitCheckoutAction : XAction
    {
        readonly StringParameter _branchName;

        public XGitCheckoutAction( Initializer initializer, ActionCollector collector )
            : base( initializer, collector )
        {
            _branchName = AddStringParameter( "BranchName", "develop" );
        }

        public override bool Run( IActivityMonitor m )
        {
            var gitFolders = NextSiblings.SelectMany( s => s.Descendants<XGitFolder>() ).Select( g => g.GitFolder );
            foreach( var git in gitFolders )
            {
                if( !git.Checkout( m, _branchName.Value ) ) return false;
            }
            return true;
        }
    }
}
