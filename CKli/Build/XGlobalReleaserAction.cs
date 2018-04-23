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
            var ctx = _solutions.GetGlobalReleaseContext( m, true );
            if( ctx == null ) return false;
            var releaser = GlobalReleaser.Create( m, ctx );
            if( releaser == null ) return false;
            XElement e = releaser.ComputeFullRoadMap( m, new ReleaseVersionSelector() );
            if( e == null ) return false;
            Console.WriteLine( "==============================================================" );
            Console.WriteLine( e.ToString() );
            return true;
        }


    }


}
