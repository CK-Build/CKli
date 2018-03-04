using CK.Text;
using System.Collections.Generic;

namespace CK.Env.Solution
{
    /// <summary>
    /// Represents a folder in a MSBuild solution.
    /// </summary>
    public sealed class SolutionFolder : ProjectBase
    {
        /// <summary>
        /// Visual Studio project type guid for solution folder
        /// </summary>
        public const string TypeIdentifier = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

        /// <summary>
        /// Gets the projects.
        /// </summary>
        public List<ProjectBase> Items { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SolutionFolder"/> class.
        /// </summary>
        /// <param name="id">The folder project identity.</param>
        /// <param name="name">The folder name.</param>
        /// <param name="path">The folder path.</param>
        public SolutionFolder( string id, string name, NormalizedPath path )
            : base( id, name, path, TypeIdentifier )
        {
            Items = new List<ProjectBase>();
        }
    }
}
