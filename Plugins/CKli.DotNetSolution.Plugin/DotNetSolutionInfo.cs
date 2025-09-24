using CK.Core;
using CKli.Core;
using System.Collections.Immutable;
using System.Collections.Generic;
using System;
using CKli.BasicDotNetSolution.Plugin;

namespace CKli.DotNetSolution.Plugin;

public sealed class DotNetSolutionInfo : RepoInfo
{
    readonly ImmutableArray<ProjectPackageReference> _refs;
    readonly BasicSolutionInfo _rawSolution;
    readonly IReadOnlyDictionary<NormalizedPath, Project> _projects;

    internal DotNetSolutionInfo( BasicSolutionInfo rawSolution, Dictionary<NormalizedPath,Project> projects, ImmutableArray<ProjectPackageReference> refs )
        : base( rawSolution.Repo )
    {
        Throw.DebugAssert( refs.Length > 0 );
        _refs = refs;
        _rawSolution = rawSolution;
        _projects = projects;
        foreach( var project in _projects.Values ) project._solution = this;
    }

    // Error ctor (issue in BasicDotNetSolution).
    internal DotNetSolutionInfo( BasicSolutionInfo rawSolution )
        : this( rawSolution, RepoInfoErrorState.Invalid, $"BasicDotNetSolution has '{rawSolution.Issue}' issue." )
    {
        Throw.DebugAssert( rawSolution.Issue is not BasicSolutionIssue.None );
    }

    // Error ctor (Project analyzis error).
    internal DotNetSolutionInfo( BasicSolutionInfo rawSolution, string anayzisErrorMessage )
        : this( rawSolution, RepoInfoErrorState.Invalid, anayzisErrorMessage )
    {
    }

    // Error ctor (Exception).
    internal DotNetSolutionInfo( BasicSolutionInfo rawSolution, Exception ex )
        : this( rawSolution, RepoInfoErrorState.Error, ex )
    {
    }

    DotNetSolutionInfo( BasicSolutionInfo rawSolution, RepoInfoErrorState errorState, object reason )
        : base( rawSolution.Repo, errorState, reason )
    {
        _refs = [];
        _projects = ImmutableDictionary<NormalizedPath, Project>.Empty;
        _rawSolution = rawSolution;
    }

    /// <summary>
    /// Gets the BasicDotNetSolution.
    /// </summary>
    public BasicSolutionInfo BasicDotNetSolution => _rawSolution;

    /// <summary>
    /// Gets the projects indexed by their <see cref="Project.ProjectFileSubPath"/>.
    /// </summary>
    public IReadOnlyDictionary<NormalizedPath, Project> Projects => _projects;

    /// <summary>
    /// Gets the package references.
    /// </summary>
    public ImmutableArray<ProjectPackageReference> PackageReferences => _refs;
}
