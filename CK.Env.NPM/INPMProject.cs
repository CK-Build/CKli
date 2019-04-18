using CK.Core;
using CK.Text;

namespace CK.Env.NPM
{
    public interface INPMProject
    {
        /// <summary>
        /// Gets the project folder path relative to the <see cref="FileSystem"/>.
        /// </summary>
        NormalizedPath FullPath { get; }

        /// <summary>
        /// Gets the package.json file object.
        /// </summary>
        PackageJsonFile PackageJson { get; }

        /// <summary>
        /// Gets the project specification.
        /// </summary>
        INPMProjectSpec Specification { get; }

        /// <summary>
        /// Gets the project status (that can be on error).
        /// </summary>
        NPMProjectStatus Status { get; }

        /// <summary>
        /// Recomputes this project status.
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        NPMProjectStatus RefreshStatus( IActivityMonitor m );
    }
}
