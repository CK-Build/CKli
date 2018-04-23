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

        protected XSolutionBase(
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
            central.Register( this );
        }

        public XSolutionCentral SolutionCentral => _central;

        public XBranch GitBranch => _branch;

        public Solution GetSolution( IActivityMonitor m, bool reload ) => GetSolution( m, FullPath, reload );

        public Solution GetSolutionInBranch( IActivityMonitor m, string branchName, bool reload )
        {
            if( branchName == _branch.Name ) return GetSolution( m, reload );
            var path = GitBranch.FullPath
                                 .RemoveLastPart()
                                 .AppendPart( branchName )
                                 .Combine( FullPath.RemovePrefix( GitBranch.FullPath ) );
            return GetSolution( m, path, reload );
        }

        protected abstract Solution GetSolution( IActivityMonitor m, NormalizedPath path, bool reload );
    }
}
