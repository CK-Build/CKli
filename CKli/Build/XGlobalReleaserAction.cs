using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SimpleGitVersion;
using CSemVer;

namespace CKli
{
    public class XGlobalReleaserAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;
        readonly XPublishedPackageFeeds _localPackages;

        public XGlobalReleaserAction(
            Initializer intializer,
            FileSystem fileSystem,
            ActionCollector collector,
            XPublishedPackageFeeds localPackages,
            XSolutionCentral solutions )
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
                if( ctx.CanCancelRelease )
                {
                    Console.Write( $"Current release can be canceled. Do you want to cancel it? (Y/N)" );
                    char a;
                    while( (a = Console.ReadKey().KeyChar) != 'Y' && a != 'N' ) ;
                    if( a == 'Y' ) return ctx.CancelRelease( m );
                }
                if( ctx.CanPublishRelease )
                {
                    Console.Write( $"Current release can be published. Do you want to publish it? (Y/N)" );
                    char a;
                    while( (a = Console.ReadKey().KeyChar) != 'Y' && a != 'N' ) ;
                    if( a == 'Y' ) return ctx.PublishRelease( m );
                }
                m.Info( $"Work in progress: {ctx.WorkStatus}. Finishing the job." );
                return ctx.ConcludeCurrentWork( m );
            }
            if( ctx.CanRelease )
            {
                return ctx.Release( m, new ReleaseVersionSelector(), false );
            }
            m.Error( $"Invalid state {ctx.WorkStatus}/{ctx.GlobalGitStatus}." );
            return false;
        }


    }


}
