using CK.Core;
using CKli.Core;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace CKli.VSSolution.Plugin;

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
    internal readonly List<string>? _candidatePaths;
    readonly IReadOnlyDictionary<string, SolutionProjectModel> _projects;
    readonly IReadOnlyList<SolutionProjectModel> _ignoreProjects;
    readonly IReadOnlyList<SolutionProjectModel> _missingProjectFiles;
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

    // Failed ctor (candidates .sln only).
    internal VSSolutionInfo( Repo repo, List<string> candidates )
        : base( repo )
    {
        _issue = candidates.Count switch
        {
            0 => VSSolutionIssue.MissingSolution,
            1 => VSSolutionIssue.BadNameSolution,
            _ => VSSolutionIssue.MultipleSolutions
        };
        _candidatePaths = candidates;
        _ignoreProjects = [];
        _missingProjectFiles = [];
        _projects = ImmutableDictionary<string, SolutionProjectModel>.Empty;
        _name = string.Empty;
    }

    // Failed ctor.
    internal VSSolutionInfo( Repo repo,
                             VSSolutionIssue issue,
                             SolutionModel solution,
                             IReadOnlyList<SolutionProjectModel>? ignoreProjects,
                             IReadOnlyList<SolutionProjectModel>? missingProjectFiles )
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
    /// Gets the solution if not <see cref="VSSolutionIssue.MissingSolution"/>, <see cref="VSSolutionIssue.MultipleSolutions"/>
    /// or <see cref="VSSolutionIssue.BadNameSolution"/>.
    /// </summary>
    public SolutionModel? Solution => _solution;


    internal World.Issue CreateIssue( ScreenType screenType )
    {
        Throw.DebugAssert( _issue is not VSSolutionIssue.None );
        IRenderable body = screenType.Unit;
        if( _issue is VSSolutionIssue.MissingSolution )
        {
            return World.Issue.CreateManual( $"No solution found. Expecting '{Repo.DisplayPath.LastPart}.sln' (or '.slnx').",
                                             body,
                                             Repo );
        }
        if( _issue is VSSolutionIssue.BadNameSolution )
        {
            // A BadNameSolution is the only one we COULD fix automatically but
            // this would require an intermediate commit in the repository (on
            // Windows to fix the case)...
            // This can be done but it's rather useless: let it be a manual action.
            Throw.DebugAssert( _candidatePaths != null && _candidatePaths.Count == 1 );
            var exist = Path.GetFileName( _candidatePaths![0].AsSpan() );
            var title = $"Single solution file '{exist}' should be '{Repo.DisplayPath.LastPart}{Path.GetExtension( exist )}' (case matters).";
            return World.Issue.CreateManual( title, body, Repo );
        }
        if( _issue is VSSolutionIssue.MultipleSolutions )
        {
            Throw.DebugAssert( _candidatePaths != null && _candidatePaths.Count > 1 );
            var title = $"Multiple solution files found. One of them must be '{Repo.DisplayPath.LastPart}.sln' (or '.slnx').";
            body = screenType.Text( $"""
                    Found: '{_candidatePaths.Select( p => Path.GetFileName( p ) ).Concatenate( "', '" )}'.
                    """ );
            return World.Issue.CreateManual( title, body, Repo );
        }
        if( _ignoreProjects.Count > 0 )
        {
            body = screenType.Text( $"""
                    Ignoring {_ignoreProjects.Count} projects:
                    {_ignoreProjects.Select( p => p.FilePath ).Concatenate()}
                    """ );
        }
        if( _issue is VSSolutionIssue.EmptySolution )
        {
            var title = $"Empty solution file.";
            return World.Issue.CreateManual( title, body, Repo );
        }
        if( (_issue & VSSolutionIssue.DuplicateProjects) != 0 )
        {
            body = body.AddBelow( screenType.Text( $"Duplicate projects found (see Warnings)" ).Box() );
        }
        if( _missingProjectFiles.Count > 0 )
        {
            body = body.AddBelow( screenType.Text( $"""
                                Missing {_missingProjectFiles.Count} declared project files:
                                {_missingProjectFiles.Select( p => p.FilePath ).Concatenate()}
                                """ ).Box() );
        }
        return World.Issue.CreateManual( "Invalid solution.", body, Repo );
    }

    /// <summary>
    /// Overridden to return this <see cref="Name"/>.
    /// </summary>
    /// <returns>The name of the solution.</returns>
    public override string ToString() => _name;
}
