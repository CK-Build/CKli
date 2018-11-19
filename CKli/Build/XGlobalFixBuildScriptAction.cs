using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli
{
    public class XGlobalFixBuildScriptAction : XAction
    {
        readonly XSolutionCentral _solutions;

        public XGlobalFixBuildScriptAction(
            Initializer intializer,
            ActionCollector collector,
            XSolutionCentral solutions )
            : base( intializer, collector )
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
            if( ctx.CanLocalFixToZeroBuildProjects )
            {
                return ctx.LocalFixToZeroBuildProjects( m );
            }
            m.Error( $"Invalid state {ctx.WorkStatus}/{ctx.GlobalGitStatus}." );
            return false;
        }

    }
}
