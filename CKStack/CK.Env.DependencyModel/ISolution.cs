using CK.Core;
using System.Collections.Generic;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Generic solution: contains a list of <see cref="IProject"/> of any type.
    /// </summary>
    public interface ISolution : IPackageReferrer, ITaggedObject
    {
        /// <summary>
        /// Gets the solution context.
        /// </summary>
        ISolutionContext Solutions { get; }

        /// <summary>
        /// Gets the full path of this solution that must be unique across <see cref="Solutions"/>.
        /// </summary>
        NormalizedPath FullPath { get; }

        /// <summary>
        /// Gets the solution name that must uniquely identify a solution among multiple solutions.
        /// This is not necessarily the last part of its <see cref="FullPath"/>.
        /// </summary>
        new string Name { get; }

        /// <summary>
        /// Gets the current version. This changes each time
        /// anything changes in this solution or its projects.
        /// </summary>
        int Version { get; }

        /// <summary>
        /// Gets all the projects.
        /// </summary>
        IReadOnlyList<IProject> Projects { get; }

        /// <summary>
        /// Gets all the generated artifacts from all <see cref="Projects"/>.
        /// </summary>
        IEnumerable<GeneratedArtifact> GeneratedArtifacts { get; }

        /// <summary>
        /// Gets the build project. Can be null.
        /// When not null, the project belongs to this <see cref="Projects"/> and both <see cref="Project.IsPublished"/>
        /// and <see cref="Project.IsTestProject"/> must be false.
        /// </summary>
        IProject? BuildProject { get; }

        /// <summary>
        /// Gets the repositories where produced artifacts must be pushed.
        /// </summary>
        IReadOnlyCollection<IArtifactRepository> ArtifactTargets { get; }

        /// <summary>
        /// Gets the artifact sources.
        /// </summary>
        IReadOnlyCollection<IArtifactFeed> ArtifactSources { get; }

        /// <summary>
        /// Gets a set of projectless dependencies.
        /// </summary>
        IReadOnlyCollection<SolutionPackageReference> SolutionPackageReferences { get; }

        /// <summary>
        /// Gets the <see cref="SolutionPackageReferences"/> concatenated to all <see cref="IProject.PackageReferences"/>.
        /// This is the whole set of references from this solution.
        /// </summary>
        IEnumerable<PackageReference> AllPackageReferences { get; }

        /// <summary>
        /// Returns the <see cref="Name"/>.
        /// </summary>
        /// <returns>The solution name.</returns>
        string ToString();

    }
}
