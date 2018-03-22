using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XSolutionCentral : XTypedObject
    {
        readonly MSBuildContext _msBuildContext;

        public class BranchSolutionList
        {
            readonly string _branchName;
            readonly List<XSolutionBase> _allSolutions;

            /// <summary>
            /// Gets the branch name.
            /// </summary>
            public string BranchName => _branchName;

            /// <summary>
            /// Gets all the solution from this <see cref="BranchName"/> defined among the next siblings.
            /// </summary>
            public IEnumerable<XSolutionBase> AllSolutions => _allSolutions;

            /// <summary>
            /// Gets the primary solutions from this <see cref="BranchName"/> defined among the next siblings.
            /// </summary>
            public IEnumerable<XPrimarySolution> PrimarySolutions => _allSolutions.OfType<XPrimarySolution>();

            /// <summary>
            /// Gets the secondary solutions from this <see cref="BranchName"/> defined among the next siblings.
            /// </summary>
            public IEnumerable<XSecondarySolution> SecondarySolutions => _allSolutions.OfType<XSecondarySolution>();

            internal BranchSolutionList( string b )
            {
                _branchName = b;
                _allSolutions = new List<XSolutionBase>();
            }

            internal void Register( XSolutionBase b ) => _allSolutions.Add( b );

        }

        readonly Dictionary<string, BranchSolutionList> _branches;

        public XSolutionCentral(
            FileSystem fileSystem,
            Initializer initializer )
            : base( initializer )
        {
            _msBuildContext = new MSBuildContext( fileSystem );
            initializer.Services.Add( this );
            _branches = new Dictionary<string, BranchSolutionList>();
        }

        internal void Register( XSolutionBase s )
        {
            if( !_branches.TryGetValue( s.GitBranch.Name, out BranchSolutionList l ) )
            {
                l = new BranchSolutionList( s.GitBranch.Name );
                _branches.Add( l.BranchName, l );
            }
            l.Register( s );
        }

        /// <summary>
        /// Gets the MSBuild context.
        /// </summary>
        public MSBuildContext MSBuildContext => _msBuildContext;


    }
}
