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
        readonly List<XSolutionBase> _allSolutions;

        public XSolutionCentral(
            FileSystem fileSystem,
            Initializer initializer )
            : base( initializer )
        {
            _msBuildContext = new MSBuildContext( fileSystem );
            initializer.Services.Add( this );
            _allSolutions = new List<XSolutionBase>();
        }

        internal void Register( XSolutionBase s ) => _allSolutions.Add( s );

        /// <summary>
        /// Gets the MSBuild context.
        /// </summary>
        public MSBuildContext MSBuildContext => _msBuildContext;

        public IEnumerable<XSolutionBase> AllSolutions => _allSolutions;

    }
}
