using CK.Env.DependencyModel;

namespace CK.Env
{
    /// <summary>
    /// Exposes the current release state of a solution in a <see cref="ReleaseRoadmap"/>.
    /// </summary>
    public interface IReleaseSolutionInfo
    {
        /// <summary>
        /// Gets the dependent solution.
        /// </summary>
        DependentSolution Solution { get; }

        /// <summary>
        /// Gets the current <see cref="ReleaseInfo"/>. May not be valid.
        /// </summary>
        ReleaseInfo CurrentReleaseInfo { get; }

        /// <summary>
        /// Gets or sets the release note.
        /// </summary>
        string? ReleaseNote { get; set; }
    }
}
