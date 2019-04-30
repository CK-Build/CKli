using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    public interface INPMProjectSpec
    {
        /// <summary>
        /// Gets the package name.
        /// This can be null if <see cref="IsPrivate"/> is true.
        /// </summary>
        string PackageName { get; }

        /// <summary>
        /// Gets whether this package must be private (ie. not published).
        /// </summary>
        bool IsPrivate { get; }

        /// <summary>
        /// Gets the folder path relative to the <see cref="FileSystem"/>.
        /// </summary>
        NormalizedPath FullPath { get; }

    }
}
