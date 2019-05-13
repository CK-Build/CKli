using CK.Core;
using CK.Env.DependencyModel;
using CSemVer;
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
        /// Gets the <see cref="ReleaseInfo"/> that has already been choosen: <see cref="ReleaseInfo.IsValid"/>
        /// is true only if this information has already been determined by a previous <see cref="ReleaseRoadmap.UpdateRoadmap"/>,
        /// however it may be invalid in the new update context: see <see cref="CanUsePreviouslyResolvedInfo"/>.
        /// </summary>
        ReleaseInfo PreviouslyResolvedInfo { get; }

        /// <summary>
        /// Gets whether the <see cref="PreviouslyResolvedInfo"/> can still be chosen.
        /// </summary>
        bool CanUsePreviouslyResolvedInfo { get; }

        /// <summary>
        /// Gets the previous released version of this solution.
        /// Null if there is no previous release.
        /// </summary>
        CSVersion PreviousVersion { get; }

        /// <summary>
        /// Gets the Sha of the commit that is the <see cref="PreviousVersion"/>.
        /// Null if there is no previous release.
        /// </summary>
        string PreviousVersionCommitSha { get; }

        /// <summary>
        /// Gets or sets the release note.
        /// </summary>
        string ReleaseNote { get; set; }

        /// <summary>
        /// Gets the current requirements that results from solution dependencies.
        /// </summary>
        ReleaseInfo Requirements { get; }

        /// <summary>
        /// Gets the possible versions for each release level.
        /// </summary>
        IReadOnlyDictionary<ReleaseLevel, IReadOnlyList<CSVersion>> PossibleVersions { get; }

        /// <summary>
        /// Gets all the distinct possible versions.
        /// </summary>
        IReadOnlyCollection<CSVersion> AllPossibleVersions { get; }

        /// <summary>
        /// Gets the set of <see cref="DirectoryDiff"/> for the project folders.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The set of diff or null on error.</returns>
        IReadOnlyCollection<DirectoryDiff> GetProjectsDiff( IActivityMonitor m );

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
        /// Sets the choice.
        /// It must be called once and only once and <see cref="Cancel"/> must not be called
        /// (neither before or after).
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
