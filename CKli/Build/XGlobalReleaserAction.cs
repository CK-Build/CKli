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
                m.Info( $"Context is already switching: {ctx.GlobalGitStatus}. Finishing the transition." );
                return ctx.ConcludeCurrentWork( m );
            }
            if( ctx.CanRelease )
            {
                return ctx.Release( m, new MasterReleaseVersionSelector(), false );
            }
            m.Error( $"Invalid state {ctx.GlobalGitStatus}." );
            return false;
        }


    }


}
