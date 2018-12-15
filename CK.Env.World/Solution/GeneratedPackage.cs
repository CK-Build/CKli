using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Captures basic ingormation related to package produced by a solution.
    /// </summary>
    public readonly struct GeneratedPackage
    {
        /// <summary>
        /// Gets the package name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the folder path of this project relative to the primary solution folder.
        /// </summary>
        public string PrimarySolutionRelativeFolderPath { get; }

        /// <summary>
        /// Initializes a new <see cref="GeneratedPackage"/>.
        /// </summary>
        /// <param name="name">Name of the package.</param>
        /// <param name="path">Pame of the package.</param>
        public GeneratedPackage( string name, string path )
        {
            Name = name;
            PrimarySolutionRelativeFolderPath = path;
        }
    }
}
