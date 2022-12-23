using CK.Core;

namespace CK.Env
{
    public interface INPMProjectSpec
    {
        /// <summary>
        /// Gets the package name.
        /// </summary>
        string PackageName { get; }

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
