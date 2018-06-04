using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CK.Core;
using CK.Env.Analysis;

namespace CKli
{
    public class XGitResetAction : XAction
    {
        public XGitResetAction( Initializer initializer, ActionCollector collector )
            : base( initializer, collector )
        {
        }

        public override bool Run( IActivityMonitor m )
        {
            Console.WriteLine( "Are you really sure to totally reset current changes on all repositories (Y/N)?" );
            char c;
            while( "YN".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) ;
            if( c == 'Y' )
            {
                var gitFolders = NextSiblings.SelectMany( s => s.Descendants<XGitFolder>() ).Select( g => g.GitFolder );
                foreach( var git in gitFolders )
                {
                    if( !git.ResetHard( m ) ) return false;
                }
            }
            return true;
        }
    }
}
