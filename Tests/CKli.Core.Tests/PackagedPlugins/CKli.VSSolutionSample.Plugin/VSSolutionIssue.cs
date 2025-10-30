using CKli.Core;
using System;

namespace CKli.VSSolutionSample.Plugin;

/// <summary>
/// The <see cref="VSSolutionInfo.Issue"/>.
/// </summary>
[Flags]
public enum VSSolutionIssue
{
    /// <summary>
    /// No issue.
    /// </summary>
    None,

    /// <summary>
    /// No ".sln" or ".slx" file exist.
    /// </summary>
    MissingSolution = 1,

    /// <summary>
    /// A single ".sln" or ".slnx" file exists but its name doesn't match the last part of the <see cref="Repo.DisplayPath"/>.
    /// The check is done case sensitively regardless of the platform.
    /// </summary>
    BadNameSolution = 2,

    /// <summary>
    /// Some ".sln" or ".slnx" files exists but none of them is named with the last part of the <see cref="Repo.DisplayPath"/>.
    /// </summary>
    MultipleSolutions = 4,

    /// <summary>
    /// A single ".sln" or ".slnx" file exists but is empty (no C# project found).
    /// </summary>
    EmptySolution = 8,

    /// <summary>
    /// A single ".sln" or ".slnx" file exists but at least one of its declared projects is not found.
    /// </summary>
    MissingProjects = 16,

    /// <summary>
    /// A single ".sln" or ".slnx" file exists but at least one of its declared projects appears twice.
    /// </summary>
    DuplicateProjects = 32
}
