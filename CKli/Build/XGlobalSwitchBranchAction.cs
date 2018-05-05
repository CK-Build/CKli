using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using CKSetup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli
{
    public class XGlobalSwitchBranchAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;
        readonly XPublishedPackageFeeds _localPackages;

        public XGlobalSwitchBranchAction(
            Initializer intializer,
            FileSystem fileSystem,
            XPublishedPackageFeeds localPackages,
            XSolutionCentral solutions,
            ActionCollector collector )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _solutions = solutions;
            _localPackages = localPackages;
        }

        public override bool Run( IActivityMonitor m )
        {
            var ctx = _solutions.GetWorldContext( m );
            if( ctx == null ) return false;
            if( ctx.HasWorkPending )
            {
                m.Info( $"Context is already switching: {ctx.GlobalGitStatus}. Finishing the transition." );
                return ctx.ConcludeCurrentWork( m );
            }
            if( ctx.CanSwitchToDevelop )
            {
                Console.Write( $"Current branch is '{ctx.World.DevelopBranchName}'. Do you want to switch to '{ctx.World.LocalBranchName}'? (Y/N)" );
                char a;
                while( (a = Console.ReadKey().KeyChar) == 'Y' || a == 'N' ) ;
                if( a == 'Y' ) return ctx.SwitchToLocal( m );
                return false;
            }
            if( ctx.CanSwitchToLocal )
            {
                Console.Write( $"Current branch is '{ctx.World.LocalBranchName}'. Do you want to switch to '{ctx.World.DevelopBranchName}'? (Y/N)" );
                char a;
                while( (a = Console.ReadKey().KeyChar) == 'Y' || a == 'N' ) ;
                if( a == 'Y' ) return ctx.SwitchToDevelop( m );
                return false;
            }
            m.Error( $"Invalid state {ctx.GlobalGitStatus}." );
            return false;
        }

    }
}
