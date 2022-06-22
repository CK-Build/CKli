using CK.Core;

namespace CK.Env
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
        /// Gets the folder path relative to the solution root.
        /// </summary>
        NormalizedPath Folder { get; }

        /// <summary>
        /// Gets the folder where the project build output.
        /// </summary>
        NormalizedPath OutputFolder { get; }

    }
}
