using CK.Core;
using CK.Env.DependencyModel;
using CSemVer;
using SimpleGitVersion;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Context for the <see cref="IReleaseVersionSelector"/> methods.
    /// </summary>
    public interface IReleaseVersionSelectorContext
    {
        /// <summary>
        /// The dependent solution being released.
        /// </summary>
        DependentSolution Solution { get; }

        /// <summary>
        /// Gets the <see cref="ReleaseInfo"/> that has already been chosen: <see cref="ReleaseInfo.IsValid"/>
        /// is true only if this information has already been determined by a previous <see cref="ReleaseRoadmap.UpdateRoadmap"/>,
        /// however it may be invalid in the new update context: see <see cref="CanUsePreviouslyResolvedInfo"/>.
        /// </summary>
        ReleaseInfo PreviouslyResolvedInfo { get; }

        /// <summary>
        /// Gets whether the <see cref="PreviouslyResolvedInfo"/> can still be chosen.
        /// </summary>
        bool CanUsePreviouslyResolvedInfo { get; }

        /// <summary>
        /// Gets the current released version of this solution.
        /// Null if there is no current release.
        /// </summary>
        ITagCommit? CurrentReleasedVersion { get; }

        /// <summary>
        /// Gets the SingleMajor repository configuration if any.
        /// </summary>
        int? SingleMajorConfigured { get; }

        /// <summary>
        /// Gets the OnlyPatch repository configuration if any.
        /// </summary>
        bool OnlyPatchConfigured { get; }

        /// <summary>
        /// Gets the previous version of this solution if there has been a previous version:
        /// when this is not null, <see cref="GetProjectsDiff(IActivityMonitor)"/> can be called.
        /// </summary>
        ITagCommit? PreviousVersion { get; }

        /// <summary>
        /// Gets the set of <see cref="DirectoryDiff"/> for the project folders between the current head
        /// and <see cref="PreviousVersion"/> that must be not null.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The set of diff or null on error.</returns>
        IDiffResult? GetProjectsDiff( IActivityMonitor m );

        /// <summary>
        /// Gets the list of the packages that must be updated in non published projects (typically in Tests projects).
        /// Empty if none.
        /// </summary>
        IReadOnlyList<(ImportedLocalPackage LocalRef, SVersion NewVersion)> PublishedUpdates { get; }

        /// <summary>
        /// Gets the list of the packages that must be updated in the published projects.
        /// Empty if none.
        /// </summary>
        IReadOnlyList<(ImportedLocalPackage LocalRef, SVersion NewVersion)> NonPublishedUpdates { get; }

        /// <summary>
        /// Gets or sets the release note.
        /// </summary>
        string? ReleaseNote { get; set; }

        /// <summary>
        /// Gets the current requirements that results from solution dependencies.
        /// Note that the actual requirements used to filer possible versions may have been lowered
        /// by <see cref="SingleMajorConfigured"/> or <see cref="OnlyPatchConfigured"/>: see <see cref="ActualRequirements"/>.
        /// </summary>
        ReleaseInfo Requirements { get; }

        /// <summary>
        /// Gets the <see cref="Requirements"/> or may be a lowered requirements if <see cref="SingleMajorConfigured"/>
        /// or <see cref="OnlyPatchConfigured"/> are set.
        /// </summary>
        ReleaseInfo ActualRequirements { get; }

        /// <summary>
        /// Gets the possible versions for each release level.
        /// </summary>
        IReadOnlyDictionary<ReleaseLevel, IReadOnlyList<CSVersion>> PossibleVersions { get; }

        /// <summary>
        /// Gets all the distinct possible versions.
        /// </summary>
        IReadOnlyCollection<CSVersion> AllPossibleVersions { get; }

        /// <summary>
        /// Cancels the current session. It can be restarted later.
        /// It must be called once and only once and <see cref="SetChoice"/> must not be called
        /// (neither before or after).
        /// </summary>
        void Cancel();

        /// <summary>
        /// Gets whether <see cref="Cancel"/> has been called.
        /// </summary>
        bool IsCanceled { get; }

        /// <summary>
        /// Gets whether <see cref="IsCanceled"/> or <see cref="HasChoice"/> is true.
        /// </summary>
        bool IsAnswered { get; }

        /// <summary>
        /// Preserves the previous choice. Can be called only if <see cref="CanUsePreviouslyResolvedInfo"/> is true.
        /// <para>
        /// <see cref="IsAnswered"/> must be false otherwise an <see cref="System.InvalidOperationException"/> is thrown.
        /// </para>
        /// </summary>
        void SetPreviouslyResolved();

        /// <summary>
        /// Sets the choice.
        /// <para>
        /// <see cref="IsAnswered"/> must be false otherwise an <see cref="System.InvalidOperationException"/> is thrown.
        /// </para>
        /// </summary>
        /// <param name="level">The chosen level.</param>
        /// <param name="version">The version among the ones of the level.</param>
        void SetChoice( ReleaseLevel level, CSVersion version );

        /// <summary>
        /// Gets whether <see cref="SetChoice"/> has been called.
        /// </summary>
        bool HasChoice { get; }

    }
}
