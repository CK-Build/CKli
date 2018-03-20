using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuild
{
    public interface IDependentProject
    {
        /// <summary>
        /// Gets the project itself.
        /// </summary>
        Project Project { get; }

        /// <summary>
        /// Whether this project belongs to the Published ones.
        /// When a project is published, its name is the one of the associated DependencyContext.Package
        /// and is necessarily unique: we can use its file name as the item's FullName (with the .csproj
        /// extension: the naked project name is used for its Package's FullName).
        /// But when a project is not published, we scope its name to its solution name to compute the
        /// FullName so that utility projects can have the same name in different solutions.
        /// </summary>
        bool IsPublished { get; }

        /// <summary>
        /// Gets the full name of this project item (see <see cref="IsPublished"/>).
        /// </summary>
        string FullName { get; }

    }
}
