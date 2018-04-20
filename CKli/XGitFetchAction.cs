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
                    if( xgit.GitFolder == null )
                    {
                        // No git folder: Clone it (single remote, default branch)
                        xgit.EnsureOrCloneGitDirectory( m );
                    }
                    else
                    {
                        // With git folder: Fetch 'origin'.
                        if( !xgit.GitFolder.FetchAll( m ) ) return false;
                    }
                }
            }
            return true;
        }
    }
}
