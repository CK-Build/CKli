using CK.Core;
using CKli.Core;
using System.Collections.Immutable;

namespace CKli.RawSolution.Plugin;

/// <summary>
/// Minimal capture of a ".sln" or ".slnx" file and its project.
/// <para>
/// This can always be computed: the <see cref="Issue"/> handles problematic
/// cases but this <see cref="RepoInfo.ErrorState"/> is always <see cref="RepoInfoErrorState.None"/>.
/// </para>
/// </summary>
public sealed class RawSolutionInfo : RepoInfo
{
    readonly RawSolutionIssue _issue;
    readonly IReadOnlyList<NormalizedPath> _duplicateProjectNames;
    readonly IReadOnlyList<NormalizedPath> _missingProjectFiles;
    readonly IReadOnlyDictionary<string, NormalizedPath> _projectFiles;
    readonly IReadOnlyList<NormalizedPath> _badFolderProjectNames;
    readonly string _name;
    readonly NormalizedPath _slnPath;

    // Success ctor.
    internal RawSolutionInfo( Repo repo,
                          Dictionary<string, NormalizedPath> projectFiles,
                          NormalizedPath slnPath,
                          IReadOnlyList<NormalizedPath>? badFolderProjectNames )
        : base( repo )
    {
        _projectFiles = projectFiles;
        _slnPath = slnPath;
        _name = slnPath.LastPart;
        _badFolderProjectNames = badFolderProjectNames ?? Array.Empty<NormalizedPath>();
        _duplicateProjectNames = Array.Empty<NormalizedPath>();
        _missingProjectFiles = Array.Empty<NormalizedPath>();
    }

    // Failed ctor.
    internal RawSolutionInfo( Repo repo,
                          RawSolutionIssue issue,
                          IReadOnlyList<NormalizedPath>? duplicateProjectNames,
                          IReadOnlyList<NormalizedPath>? missingProjectFiles )
        : base( repo )
    {
        _issue = issue;
        _duplicateProjectNames = duplicateProjectNames ?? Array.Empty<NormalizedPath>();
        _missingProjectFiles = missingProjectFiles ?? Array.Empty<NormalizedPath>();
        _projectFiles = ImmutableDictionary<string,NormalizedPath>.Empty;
        _badFolderProjectNames = Array.Empty<NormalizedPath>();
        _name = string.Empty;
    }

    /// <summary>
    /// Gets issue for this solution.
    /// </summary>
    public RawSolutionIssue Issue => _issue;

    /// <summary>
    /// Gets the short name of this solution.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the ".sln" or ".slnx" file path.
    /// Can be if the empty path <see cref="Issue"/> is not <see cref="RawSolutionIssue.None"/>.
    /// </summary>
    public NormalizedPath SlnPath => _slnPath;

    /// <summary>
    /// Gets the projects file path indexed by their name (without ".csproj" extension).
    /// The project file name MUST be the name of the produced NuGet package if this project is packable.
    /// </summary>
    public IReadOnlyDictionary<string, NormalizedPath> ProjectFiles => _projectFiles;

    /// <summary>
    /// Gets a list of ".csproj" files that are in a folder with a different name.
    /// </summary>
    public IReadOnlyList<NormalizedPath> BadFolderProjectNames => _badFolderProjectNames;

    public override string ToString() => _name;
}
