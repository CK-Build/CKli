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

namespace CKli
{
    public class XGlobalLocalBuildAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;
        readonly XLocalFeedProvider _localPackages;

        public XGlobalLocalBuildAction(
            Initializer intializer,
            FileSystem fileSystem,
            XLocalFeedProvider localPackages,
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
            var ctx = _solutions.InitializeWorldContext( m );
            if( ctx == null ) return false;
            if( ctx.IsConcludeCurrentWorkEnabled )
            {
                m.Info( $"Work in progress: {ctx.WorkStatus}. Finishing the job. Press Enter." );
                Console.ReadLine();
                return ctx.ConcludeCurrentWork( m );
            }
            m.Info( $"Current Global Status: {ctx.GlobalGitStatus}." );
            if( ctx.CanRunCIBuild ) return ctx.RunCIBuild( m );

            m.Error( $"Invalid state {ctx.WorkStatus}/{ctx.GlobalGitStatus}." );
            return false;
        }


    }


}