using CK.Core;
using CKli.Core;
using CKli.RawSolution.Plugin;
using System.Collections.Immutable;

namespace CKli.DotNetSolution.Plugin;

public sealed class DotNetSolutionInfo : RepoInfo
{
    readonly ImmutableArray<ProjectPackageReference> _refs;
    readonly RawSolutionInfo _rawSolution;
    readonly IReadOnlyDictionary<NormalizedPath, Project> _projects;

    internal DotNetSolutionInfo( RawSolutionInfo rawSolution, Dictionary<NormalizedPath,Project> projects, ImmutableArray<ProjectPackageReference> refs )
        : base( rawSolution.Repo )
    {
        Throw.DebugAssert( refs.Length > 0 );
        _refs = refs;
        _rawSolution = rawSolution;
        _projects = projects;
        foreach( var project in _projects.Values ) project._solution = this;
    }

    // Error ctor (issue in RawSolution).
    internal DotNetSolutionInfo( RawSolutionInfo rawSolution )
        : this( rawSolution, RepoInfoErrorState.Invalid, $"RawSolution has '{rawSolution.Issue}' issue." )
    {
        Throw.DebugAssert( rawSolution.Issue is not RawSolutionIssue.None );
    }

    // Error ctor (Project analyzis error).
    internal DotNetSolutionInfo( RawSolutionInfo rawSolution, string anayzisErrorMessage )
        : this( rawSolution, RepoInfoErrorState.Invalid, anayzisErrorMessage )
    {
    }

    // Error ctor (Exception).
    internal DotNetSolutionInfo( RawSolutionInfo rawSolution, Exception ex )
        : this( rawSolution, RepoInfoErrorState.Error, ex )
    {
    }

    DotNetSolutionInfo( RawSolutionInfo rawSolution, RepoInfoErrorState errorState, object reason )
        : base( rawSolution.Repo, errorState, reason )
    {
        _refs = [];
        _projects = ImmutableDictionary<NormalizedPath, Project>.Empty;
        _rawSolution = rawSolution;
    }

    /// <summary>
    /// Gets the RawSolution.
    /// </summary>
    public RawSolutionInfo RawSolution => _rawSolution;

    /// <summary>
    /// Gets the projects indexed by their <see cref="Project.ProjectFileSubPath"/>.
    /// </summary>
    public IReadOnlyDictionary<NormalizedPath, Project> Projects => _projects;

    /// <summary>
    /// Gets the package references.
    /// </summary>
    public ImmutableArray<ProjectPackageReference> PackageReferences => _refs;
}
