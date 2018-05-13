using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CK.Core;
using CK.Env.Analysis;
using LibGit2Sharp;

namespace CKli
{
    public class XGitFetchAction : XAction
    {
        public XGitFetchAction( Initializer initializer, ActionCollector collector )
            : base( initializer, collector )
        {
        }

        public override bool Run( IActivityMonitor m )
        {
            var xGitFolders = NextSiblings.SelectMany( s => s.Descendants<XGitFolder>() );
            foreach( var xgit in xGitFolders )
            {
                using( m.OpenInfo( $"Fetching {xgit.Name} 'origin' remote" ) )
                {
                    // With git folder: Fetch 'origin'.
                    if( !xgit.GitFolder.PullAll( m ) ) return false;
                }
            }
            return true;
        }
    }
}
