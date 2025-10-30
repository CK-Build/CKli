using CK.Core;
using CKli.Core;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.VSSolutionSample.Plugin;

/// <summary>
/// Minimal capture of a ".sln" or ".slnx" file and its project.
/// <para>
/// This can always be computed: the <see cref="Issue"/> handles problematic
/// cases but this <see cref="RepoInfo.ErrorState"/> is always <see cref="RepoInfoErrorState.None"/>.
/// </para>
/// </summary>
public sealed class VSSolutionInfo : RepoInfo
{
    readonly VSSolutionIssue _issue;
    readonly IReadOnlyDictionary<string, SolutionProjectModel> _projects;
    readonly IReadOnlyList<SolutionProjectModel> _ignoreProjects;
    readonly IReadOnlyList<NormalizedPath> _missingProjectFiles;
    readonly string _name;
    readonly NormalizedPath _slnPath;
    readonly SolutionModel? _solution;

    // Success ctor.
    internal VSSolutionInfo( Repo repo,
                             NormalizedPath slnPath,
                             SolutionModel solution,
                             Dictionary<string, SolutionProjectModel> projects,
                             IReadOnlyList<SolutionProjectModel>? ignoreProjects )
        : base( repo )
    {
        _projects = projects;
        _slnPath = slnPath;
        _solution = solution;
        _name = slnPath.LastPart;
        _ignoreProjects = ignoreProjects ?? [];
        _missingProjectFiles = [];
    }

    // Failed ctor.
    internal VSSolutionInfo( Repo repo,
                          VSSolutionIssue issue,
                          SolutionModel? solution,
                          IReadOnlyList<SolutionProjectModel>? ignoreProjects,
                          IReadOnlyList<NormalizedPath>? missingProjectFiles )
        : base( repo )
    {
        _issue = issue;
        _solution = solution;
        _ignoreProjects = ignoreProjects ?? [];
        _missingProjectFiles = missingProjectFiles ?? [];
        _projects = ImmutableDictionary<string, SolutionProjectModel>.Empty;
        _name = string.Empty;
    }

    /// <summary>
    /// Gets issue for this solution.
    /// </summary>
    public VSSolutionIssue Issue => _issue;

    /// <summary>
    /// Gets the short name of this solution.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the ".sln" or ".slnx" file path.
    /// Can be if the empty path <see cref="Issue"/> is not <see cref="VSSolutionIssue.None"/>.
    /// </summary>
    public NormalizedPath SlnPath => _slnPath;

    /// <summary>
    /// Gets the projects file path indexed by their name (without ".csproj" extension).
    /// The project file name MUST be the name of the produced NuGet package if this project is packable.
    /// </summary>
    public IReadOnlyDictionary<string, SolutionProjectModel> Projects => _projects;

    /// <summary>
    /// Gets the solution if not <see cref="VSSolutionIssue.MissingSolution"/>, <see cref="VSSolutionIssue.MultipleSolution"/>
    /// or <see cref="VSSolutionIssue.BadNameSolution"/>.
    /// </summary>
    public SolutionModel? Solution => _solution;

    /// <summary>
    /// Overridden to return this <see cref="Name"/>.
    /// </summary>
    /// <returns>The name of the solution.</returns>
    public override string ToString() => _name;
}
