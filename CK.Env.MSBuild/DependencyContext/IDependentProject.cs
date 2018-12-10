using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Describes a project in a <see cref="DependencyAnalyser"/> context.
    /// </summary>
    public interface IDependentProject
    {
        /// <summary>
        /// Gets the project itself.
        /// </summary>
        Project Project { get; }

        /// <summary>
        /// Whether this project belongs to the Published ones.
        /// When a project is published, to distinguish it from the associated necessarily
        /// unique DependencyContext.Package we use its file name as the item's FullName (with the .csproj
        /// extension: the naked project name is used for its Package's FullName).
        /// But when a project is not published, we scope its file name to its solution name to compute the
        /// FullName so that utility projects can have the same name in different solutions.
        /// </summary>
        bool IsPublished { get; }

        /// <summary>
        /// Gets the full name of this project item (see <see cref="IsPublished"/>).
        /// </summary>
        string FullName { get; }

        /// <summary>
        /// Gets the name of the package produced by this project or null when <see cref="IsPublished"/> is false.
        /// When not null, this is the name of the project folder.
        /// </summary>
        string PublishedName { get; }

    }
}
