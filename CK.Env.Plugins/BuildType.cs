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
        /// Purely local build on 'local' branch.
        /// </summary>
        Local = IsTargetLocal,

        /// <summary>
        /// Switching from 'local' to 'develop' branch.
        /// The working folder is on 'develop'.
        /// </summary>
        SwitchToDevelop = IsTargetDevelop | IsUsingDirtyFolder,

        /// <summary>
        /// Local only CI build on 'develop' branch. Artefacts are kept locally.
        /// </summary>
        Develop = IsTargetDevelop | IsUsingDirtyFolder | 128,

        /// <summary>
        /// CI build on 'develop'. Artefacts are published to remotes.
        /// </summary>
        DevelopWithRemotes = IsTargetDevelop,


    }
}
