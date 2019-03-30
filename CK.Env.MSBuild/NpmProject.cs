using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    public class NpmProject
    {
        /// <summary>
        /// Initialzes a new Npm project instance.
        /// </summary>
        /// <param name="s">The holding solution.</param>
        /// <param name="subPathFolder">The project <see cref="SubPath"/>.</param>
        public NpmProject( Solution s, NormalizedPath subPathFolder )
        {
            Solution = s;
            SubPath = subPathFolder;
            FullPath = s.SolutionFolderPath.Combine( subPathFolder );
        }

        /// <summary>
        /// Gets or sets whether the package is private (ie. not published).
        /// Defaults to false.
        /// </summary>
        public bool IsPrivate { get; set; }

        /// <summary>
        /// Gets the solution to which this <see cref="NpmProject"/> belongs.
        /// </summary>
        public Solution Solution { get; }

        /// <summary>
        /// Gets the path to the project folder relative to the <see cref="Solution"/>.
        /// </summary>
        public NormalizedPath SubPath { get; }

        /// <summary>
        /// Gets the path to the project folder relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath FullPath { get; }
    }
}
