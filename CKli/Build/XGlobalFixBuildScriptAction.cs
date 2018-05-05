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
            var ctx = _solutions.GetWorldContext( m );
            if( ctx == null ) return false;
            if( ctx.HasWorkPending )
            {
                m.Info( $"Context is switching: {ctx.GlobalGitStatus}. Finishing the transition." );
                return ctx.ConcludeCurrentWork( m );
            }
            if( ctx.CanLocalFixToZeroBuildProjects )
            {
                return ctx.LocalFixToZeroBuildProjects( m );
            }
            m.Error( $"Must be on {ctx.World.LocalBranchName} branch." );
            return false;
        }

    }
}
