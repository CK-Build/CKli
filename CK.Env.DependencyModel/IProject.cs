using System.Collections.Generic;
using CK.Core;
using CK.Text;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Project belongs to a <see cref="ISolution"/>. It defines its <see cref="GeneratedArtifacts"/>, <see cref="PackageReferences"/>,
    /// <see cref="ProjectReferences"/> and whether it is a <see cref="IsTestProject"/>, <see cref="IsPublished"/> or whether it is
    /// the <see cref="Solution.BuildProject"/> or not.
    /// </summary>
    public interface IProject : ITaggedObject
    {
        /// <summary>
        /// Gets the solution to which this project belongs.
        /// This is null if this project has been removed from its Solution.
        /// </summary>
        ISolution Solution { get; }

        /// <summary>
        /// Gets the full path to this project folder that starts with the <see cref="Solution.FullPath"/>.
        /// </summary>
        NormalizedPath FullFolderPath { get; }

        /// <summary>
        /// Gets the path to this project relative to the <see cref="Solution"/>.
        /// </summary>
        NormalizedPath SolutionRelativeFolderPath { get; }

        /// <summary>
        /// Gets a set of full paths (folder or files) that are "sources" for this project.
        /// By default, <see cref="SolutionRelativeFolderPath"/> is systematically added to this set.
        /// Any file and or folder that are outside the project folder should be added to this
        /// set (typically files or folders shared accross multiple projects).
        /// This is used to detect changes for project (typically thanks to Git history).
        /// </summary>
        IReadOnlyCollection<NormalizedPath> ProjectSources { get; }

        /// <summary>
        /// Gets the simple project name that may be ambiguous: see <see cref="Name"/>.
        /// </summary>
        string SimpleProjectName { get; }

        /// <summary>
        /// Gets the project type: basically supported types are ".Net" and "js".
        /// </summary>
        string Type { get; }

        /// <summary>
        /// Gets the savors of this project. This is null by default.
        /// When not null:
        /// <para>
        /// - this trait can not be the empty one (see <see cref="CKTrait.IsEmpty"/>).
        /// </para>
        /// <para>
        /// - the <see cref="CKTrait.Context"/> can be any context (typically
        /// the <see cref="ArtifactType.ContextSavors"/> of the "primary" artifact produced by this project).
        /// </para>
        /// <para>
        /// <see cref="PackageReference.ApplicableSavors"/> and <see cref="ProjectReference.ApplicableSavors"/> share
        /// the same context and are either equal to these savors or are a subset of them (and cannot be empty either).
        /// </para>
        /// </summary>
        CKTrait Savors { get; }

        /// <summary>
        /// Gets all artifacts that this project generates. 
        /// </summary>
        IReadOnlyList<GeneratedArtifact> GeneratedArtifacts { get; }

        /// <summary>
        /// Gets or sets whether this project is the <see cref="Solution.BuildProject"/>.
        /// </summary>
        bool IsBuildProject { get; set; }

        /// <summary>
        /// Gets whether this project is published: this project can be "installed" in
        /// dependent solutions thanks to at least one <see cref="GeneratedArtifacts"/>
        /// that is <see cref="ArtifactType.IsInstallable"/>.
        /// </summary>
        bool IsPublished { get; }

        /// <summary>
        /// Gets whether this project is published: this project can be "installed" in
        /// dependent solutions thanks to at least one <see cref="GeneratedArtifacts"/>
        /// that is <see cref="ArtifactType.IsInstallable"/>.
        /// </summary>
        bool IsTestProject { get; }

        /// <summary>
        /// Gets the project name: it can be the <see cref="SimpleProjectName"/>, prefixed or
        /// not by the <see cref="Solution.Name"/> and automatically prefixed by (<see cref="Type"/>)
        /// or with <see cref="SolutionRelativeFolderPath"/> in case of conflicts
        /// with other projects in the <see cref="SolutionContext"/>.
        /// Given that the solution name is unique by design and that only one project of a given type can
        /// be defined in a folder, this name is eventually always unique. 
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the package references.
        /// </summary>
        IReadOnlyList<PackageReference> PackageReferences { get; }

        /// <summary>
        /// Gets the references to local projects.
        /// </summary>
        IReadOnlyList<ProjectReference> ProjectReferences { get; }
    }
}
