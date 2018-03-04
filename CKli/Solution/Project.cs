using CK.Text;
using System.Collections.Generic;

namespace CK.Env.Solution
{
    /// <summary>
    /// Represents an actual project in a solution.
    /// </summary>
    public class Project : ProjectBase
    {
        /// <summary>
        /// Initializes a new <see cref="Project"/> instance.
        /// </summary>
        /// <param name="id">The folder project identity.</param>
        /// <param name="name">The folder name.</param>
        /// <param name="path">The folder path.</param>
        public Project( string id, string name, NormalizedPath projectFilePath, string typeIdentifier )
            : base( id, name, projectFilePath, typeIdentifier )
        {
        }

        public bool IsCSProj => Path.LastPart.EndsWith( ".csproj" );


        public IReadOnlyList<string> TargetFrameworks { get; private set; }

    }
}
