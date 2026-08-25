using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Immutable;

namespace CKli.Publish.Plugin;

/// <summary>
/// 
/// </summary>
public sealed class PublishRepoInfo
{
    readonly Roadmap.BuildSolution _solution;

    internal PublishRepoInfo( Roadmap.BuildSolution solution )
    {
        _solution = solution;
    }

    public Repo Repo => _solution.Repo;

}

