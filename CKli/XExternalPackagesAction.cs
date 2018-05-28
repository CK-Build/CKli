using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XExternalPackagesAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;

        public XExternalPackagesAction(
            Initializer intializer,
            FileSystem fileSystem,
            ActionCollector collector,
            XSolutionCentral solutions )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _solutions = solutions;
        }

        public override bool Run( IActivityMonitor m )
        {
            var ctx = _solutions.GetWorldContext( m );
            if( ctx == null ) return false;
            if( !ctx.CanCreateSolutionDependencyResult )
            {
                m.Error( $"Context status is '{ctx.WorkStatus}'. It must be 'Idle'." );
                return false;
            }
            var deps = ctx.CreateSolutionDependencyResult( m );
            if( deps == null ) return false;
            deps.ProjectDependencies.GetExternalDependencies();
            return true;
        }
    }
}
