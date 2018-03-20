using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuild
{
    public enum SolutionSortStrategy
    {
        /// <summary>
        /// Consider only published projects of primary solutions
        /// (secondary solutions are ignored).
        /// </summary>
        PublishedProjects = 1,

        /// <summary>
        /// Consider published and tests projects of primary solutions
        /// (secondary solutions are ignored).
        /// </summary>
        PublishedAndTestsProjects = 2,

        /// <summary>
        /// Consider all projects and the secondary solutions if any.
        /// Build projects are ignored.
        /// </summary>
        EverythingExceptBuildProjects = 3
    }
}
