using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using CK.Env.Solution;
using CK.Env.Analysis;

namespace CKli
{
    public class XStandardSolution : XPathItem
    {
        readonly XSolutionCentral _central;
        readonly XBranch _branch;
        SolutionFile _file;

        public XStandardSolution(
            Initializer initializer,
            XBranch branch,
            XSolutionCentral central )
            : base( initializer,
                    branch.FileSystem,
                    FileSystemItemKind.File,
                    branch.FullPath.AppendPart( branch.Parent.Name + ".sln" ) )
        {
            _central = central;
            _branch = branch;
            initializer.ChildServices.Add( this );
        }

        public XBranch GitBranch => _branch;

        public SolutionFile ReadSolutionFile( IActivityMonitor m, bool force = false )
        {
            if( _file == null || force )
            {
                _file = _central.GetSolution( m, FullPath, force );
            }
            return _file;
        }

        protected override void Reset( IRunContext ctx )
        {
            _file = null;
            base.Reset( ctx );
        }

        protected override bool DoRun( IRunContext ctx )
        {
            ReadSolutionFile( ctx.Monitor );
            return base.DoRun( ctx );
        }
    }
}
