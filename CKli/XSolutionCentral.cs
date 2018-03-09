using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CKli
{
    public class XSolutionCentral : XTypedObject
    {
        readonly ProjectFileContext _projectContext;
        readonly Dictionary<NormalizedPath, SolutionFile> _solutions;

        public XSolutionCentral(
            FileSystem fileSystem,
            Initializer initializer )
            : base( initializer )
        {
            _solutions = new Dictionary<NormalizedPath, SolutionFile>();
            _projectContext = new ProjectFileContext( fileSystem );
            initializer.Services.Add( this );
        }

        public SolutionFile GetSolution( IActivityMonitor m, NormalizedPath path, bool forceReload )
        {
            if( forceReload || !_solutions.TryGetValue( path, out var solution ) )
            {
                solution = SolutionFile.Create( m, _projectContext, path );
                if( solution == null ) return null;
                _solutions[path] = solution;
            }
            return solution;
        }

    }
}
