using CK.Core;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using System.Collections.Generic;

namespace CK.Env.Solution
{
    /// <summary>
    /// Represents an actual project in a solution.
    /// </summary>
    public class Project : ProjectBase
    {
        readonly ProjectFileContext _ctx;
        ProjectFileContext.File _file;

        /// <summary>
        /// Initializes a new <see cref="Project"/> instance.
        /// </summary>
        /// <param name="id">The folder project identity.</param>
        /// <param name="name">The folder name.</param>
        /// <param name="path">The folder path.</param>
        public Project( string id, string name, NormalizedPath projectFilePath, string typeIdentifier, ProjectFileContext ctx )
            : base( id, name, projectFilePath, typeIdentifier )
        {
            _ctx = ctx;
        }

        public bool IsCSProj => Path.LastPart.EndsWith( ".csproj" );

        /// <summary>
        /// Gets the project file. This is loaded when the <see cref="SolutionFile"/>
        /// is created but may be reloaded.
        /// This is null if an error occurred while loading.
        /// </summary>
        public ProjectFileContext.File ProjectFile => _file;

        public ProjectFileContext.File LoadProjectFile( IActivityMonitor m, bool force = false )
        {
            if( !force && _file != null ) return _file;
            _file = _ctx.FindOrLoad( m, Path, force );
            return _file;
        }

        public IReadOnlyList<string> TargetFrameworks { get; private set; }

    }
}
