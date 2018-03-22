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
            central.Register( this );
        }

        public XBranch GitBranch => _branch;

        public abstract Solution Solution { get; }

    }
}
