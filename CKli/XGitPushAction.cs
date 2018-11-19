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
        readonly XSolutionCentral _solutions;

        public XGitPushAction(
            Initializer initializer,
            XSolutionCentral solutions,
            ActionCollector collector )
            : base( initializer, collector )
        {
            _solutions = solutions;
        }

        public override bool Run( IActivityMonitor m )
        {
            var ctx = _solutions.InitializeWorldContext( m );
            if( ctx == null ) return false;
            if( ctx.IsConcludeCurrentWorkEnabled )
            {
                m.Info( $"Work in progress: {ctx.WorkStatus}. Finishing the job." );
                return ctx.ConcludeCurrentWork( m );
            }
            if( ctx.CurrentBranchName != ctx.World.DevelopBranchName )
            {
                m.Info( $"Context must be on {ctx.World.DevelopBranchName}." );
                return false;
            }
            Console.WriteLine( $"Are you really sure to push current changes of all repositories/{ctx.CurrentBranchName} (Y/N)?" );
            char c;
            while( "YN".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) ;
            if( c == 'Y' )
            {
                var gitFolders = NextSiblings.SelectMany( s => s.Descendants<XGitFolder>() ).Select( g => g.GitFolder );
                foreach( var git in gitFolders )
                {
                    if( !git.Push( m, ctx.CurrentBranchName ) ) return false;
                }
            }
            return true;
        }
    }
}
