using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Characterize a build.
    /// </summary>
    public enum BuildType
    {
        /// <summary>
        /// Whether the build targets the Local environment.
        /// </summary>
        IsTargetLocal = 1,

        /// <summary>
        /// Whether the build targets the Develop environment.
        /// </summary>
        IsTargetDevelop = 2,

        /// <summary>
        /// Whether the build targets the Release environment.
        /// </summary>
        IsTargetRelease = 4,

        /// <summary>
        /// Whether the build  requires to temporarily alter the working folder:
        /// a <see cref="GitFolder.ResetHard"/> will be done after the build.
        /// </summary>
        IsUsingDirtyFolder = 8,

        /// <summary>
        /// Whether the ZeroVersion is used. This requires an access to the <see cref="IEnvLocalFeedProvider.ZeroBuildFeed"/>.
        /// </summary>
        WithZeroBuilder = IsUsingDirtyFolder | 16,

        /// <summary>
        /// Purely local build on 'local' branch.
        /// This necessarily use the builder without any local modifications (<see cref="IsUsingDirtyFolder"/> is not set).
        /// </summary>
        Local = IsTargetLocal,

        /// <summary>
        /// Local only CI build on 'develop' branch. Artefacts are kept locally.
        /// </summary>
        Develop = IsTargetDevelop | IsUsingDirtyFolder,

        /// <summary>
        /// Local build with the Zero builder.
        /// </summary>
        LocalWithZeroBuilder = Local | WithZeroBuilder,

        /// <summary>
        /// Local only CI build on 'develop' branch with the Zero builder. Artefacts are kept locally.
        /// </summary>
        DevelopWithZeroBuilder = Develop | WithZeroBuilder,

        /// <summary>
        /// CI build on 'develop'. Artefacts are published to remotes.
        /// This necessarily use the builder without any local modifications (<see cref="IsUsingDirtyFolder"/> is not set).
        /// </summary>
        DevelopWithRemotes = IsTargetDevelop | 128,



    }
}
