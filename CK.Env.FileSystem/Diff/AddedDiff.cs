using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Diff
{
    class AddedDiff : IDiff, IAddedDiff
    {
        public AddedDiff( NormalizedPath path )
        {
            Path = path;
        }

        /// <summary>
        /// Path of the created file.
        /// </summary>
        public NormalizedPath Path { get; }

        public bool SendToBuilder( IActivityMonitor m, DiffRootResultBuilderBase diffRootResultBuilder )
        {
            return diffRootResultBuilder.Accept( m, this );
        }
    }
}
