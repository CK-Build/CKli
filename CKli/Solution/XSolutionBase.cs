using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using CK.Env.MSBuild;
using CK.Env.Analysis;
using CK.Text;

namespace CKli
{
    public abstract class XSolutionBase : XPathItem
    {
        readonly XSolutionCentral _central;
        readonly XBranch _branch;
        SolutionFile _solution;

        public XSolutionBase(
            Initializer initializer,
            XBranch branch,
            XSolutionCentral central,
            NormalizedPath branchBasedSolutionFilePath )
            : base( initializer,
                    branch.FileSystem,
                    FileSystemItemKind.File,
                    branch.FullPath.Combine( branchBasedSolutionFilePath ) )
        {
            _central = central;
            _branch = branch;
            initializer.ChildServices.Add( this );
        }

        public XBranch GitBranch => _branch;

        public SolutionFile ReadSolutionFile( IActivityMonitor m, bool force = false )
        {
            if( _solution == null || force )
            {
                _solution = _central.GetSolution( m, FullPath, force );
            }
            return _solution;
        }

        protected override void Reset( IRunContext ctx )
        {
            _solution = null;
            base.Reset( ctx );
        }

        protected override bool DoRun( IRunContext ctx )
        {
            ReadSolutionFile( ctx.Monitor );
            return base.DoRun( ctx );
        }
    }
}
