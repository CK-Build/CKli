using System.Collections.Generic;
using CK.Core;
using CSemVer;

namespace CK.Env
{
    public interface IReleaseVersionSelector
    {
        /// <summary>
        /// This method must choose between possible versions. It may return null to cancel the process.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="context">The context.</param>
        void ChooseFinalVersion( IActivityMonitor m, IReleaseVersionSelectorContext context );

        /// <summary>
        /// Called whenever the final version is already set by a version tag on the commit point.
        /// We consider this version to be already released.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="version">The tagged version.</param>
        /// <param name="isContentTag">False if it the tag is on the commit point, true if the tag is a content tag.</param>
        /// <returns>True to continue, false to cancel the current session.</returns>
        bool OnAlreadyReleased( IActivityMonitor m, IDependentSolution solution, CSVersion version, bool isContentTag );
    }
}
