using System;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Common solution specification. These settings are propagated accross the
    /// Xml configuration file and can be merged/altered.
    /// Common specification captures any settings that must apply globally to multiple solutions.
    /// Configuration specific to a solution (like the project it contains or the artifacts
    /// it generates) are defined on the <see cref="SolutionSpec"/>.
    /// Any implementation of this interface MUST be immutable.
    /// </summary>
    public interface ISharedSolutionSpec
    {
        /// <summary>
        /// Gets whether source link is disabled.
        /// Impacts Common/Shared.props file.
        /// Defaults to false.
        /// </summary>
        bool DisableSourceLink { get; }

        /// <summary>
        /// Gets whether the solution has no unit tests.
        /// Defaults to false.
        /// </summary>
        bool NoDotNetUnitTests { get; }

        /// <summary>
        /// Gets whether no strong name singing should be used.
        /// Defaults to false.
        /// </summary>
        bool NoStrongNameSigning { get; }

        /// <summary>
        /// Gets whether no shared props file should be used.
        /// Defaults to false.
        /// </summary>
        bool NoSharedPropsFile { get; }

        /// <summary>
        /// Defines the set of NuGet sources that is used.
        /// Impacts NuGet.config file.
        /// </summary>
        IReadOnlyCollection<INuGetSource> NuGetSources { get; }

        /// <summary>
        /// Gets the NuGet source names that must be excluded.
        /// Must be used to clean up existing source names that must no more be used.
        /// Impacts NuGet.config file.
        /// </summary>
        IReadOnlyCollection<string> RemoveNuGetSourceNames { get; }

        /// <summary>
        /// Defines the set of NPM sources that is used.
        /// Impacts .npmrc file.
        /// </summary>
        IReadOnlyCollection<INPMSource> NPMSources { get; }

        /// <summary>
        /// Gets the NPM scope names that must be excluded.
        /// Must be used to clean up existing scope names that must no more be used.
        /// Impacts .npmrc file.
        /// </summary>
        IReadOnlyCollection<string> RemoveNPMScopeNames { get; }

        /// <summary>
        /// Gets the repositories where produced artifacts must be pushed.
        /// </summary>
        IReadOnlyCollection<IArtifactRepository> ArtifactTargets { get; }

        /// <summary>
        /// Defines the set of Git or GitBranch plugins that must NOT be activated.
        /// By default, all available Git plugins are active.
        /// </summary>
        IReadOnlyCollection<Type> ExcludedPlugins { get; }

    }
}
