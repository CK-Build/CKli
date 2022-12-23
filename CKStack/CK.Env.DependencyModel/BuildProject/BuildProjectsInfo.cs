using CK.Setup;
using System.Collections.Generic;
using System.Diagnostics;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Captures Build Projects information that allows to handle ZeroVersion builds and references.
    /// This considers build projects and their dependencies without Solution containment.
    /// </summary>
    public class BuildProjectsInfo
    {
        internal BuildProjectsInfo( IDependencySorterResult sortResult,
                                    IReadOnlyList<ZeroBuildProjectInfo>? zeroBuildProjects )
        {
            Debug.Assert( sortResult != null && sortResult.IsComplete == (zeroBuildProjects != null) );
            RawBuildProjectsInfoSorterResult = sortResult;
            ZeroBuildProjects = zeroBuildProjects;
        }

        /// <summary>
        /// Gets the <see cref="IDependencySorterResult"/> of the build projects graph.
        /// Never null.
        /// </summary>
        public IDependencySorterResult RawBuildProjectsInfoSorterResult { get; }

        /// <summary>
        /// Gets whether build projects have been successfully ordered.
        /// </summary>
        public bool HasError => !RawBuildProjectsInfoSorterResult.IsComplete;

        /// <summary>
        /// Gets the ordered list of <see cref="ZeroBuildProjectInfo"/>.
        /// Null if <see cref="HasError"/> is true.
        /// </summary>
        public IReadOnlyList<ZeroBuildProjectInfo>? ZeroBuildProjects { get; }
    }
}
