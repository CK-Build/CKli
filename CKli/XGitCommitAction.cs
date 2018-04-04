using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CK.Core;
using CK.Env.Analysis;

namespace CKli
{
    public class XGitCommitAction : XAction
    {
        StringParameter _commitMessage;

        public XGitCommitAction( Initializer initializer, ActionCollector collector )
            : base( initializer, collector )
        {
            _commitMessage = AddStringParameter( "CommitMessage",
                ( m, s ) =>
                {
                    if( String.IsNullOrWhiteSpace( s ) )
                    {
                        m.Error( "Commit message can not be empty." );
                        return false;
                    }
                    return true;
                } );
        }

        public override bool Run( IActivityMonitor m )
        {
            var gitFolders = NextSiblings.SelectMany( s => s.Descendants<XGitFolder>() ).Select( g => g.GitFolder );
            foreach( var git in gitFolders )
            {
                if( !git.Commit( m, _commitMessage.Value ).Success ) return false;
            }
            return true;
        }
    }
}
