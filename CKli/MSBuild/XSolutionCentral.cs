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
        readonly GlobalContext.World _world;
        readonly MSBuildContext _msBuildContext;
        readonly List<XSolutionBase> _allSolutions;
        readonly List<XSolutionBase> _allDevelopSolutions;

        public XSolutionCentral(
            FileSystem fileSystem,
            GlobalContext.World world,
            Initializer initializer )
            : base( initializer )
        {
            _world = world;
            _msBuildContext = new MSBuildContext( fileSystem );
            initializer.Services.Add( this );
            _allSolutions = new List<XSolutionBase>();
            _allDevelopSolutions = new List<XSolutionBase>();
        }

        internal void Register( XSolutionBase s )
        {
            _allSolutions.Add( s );
            if( s.GitBranch.Name == _world.DevelopBranchName ) _allDevelopSolutions.Add( s );
        }

        /// <summary>
        /// Gets the current world.
        /// </summary>
        public GlobalContext.World World => _world;

        /// <summary>
        /// Gets the MSBuild context that handles solution and project files.
        /// </summary>
        public MSBuildContext MSBuildContext => _msBuildContext;

        /// <summary>
        /// Gets all the solutions regardless of their type or branch.
        /// </summary>
        public IReadOnlyList<XSolutionBase> AllSolutions => _allSolutions;

        /// <summary>
        /// Gets all the solutions regardless of their type in branch <see cref="World"/>.<see cref="GlobalContext.World.DevelopBranchName">DevelopBranchName</see>.
        /// </summary>
        public IReadOnlyList<XSolutionBase> AllDevelopSolutions => _allDevelopSolutions;

    }
}
