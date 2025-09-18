using CK.Core;
using System.Diagnostics.CodeAnalysis;

namespace CKli.DotNetSolution.Plugin;

public sealed class Project
{
    readonly NormalizedPath _projectFileSubPath;
    [AllowNull] internal DotNetSolutionInfo _solution;
    internal bool _isPackable;

    internal Project( NormalizedPath projectFileSubPath,
                      bool isPackable )
    {
        _projectFileSubPath = projectFileSubPath;
        _isPackable = isPackable;
    }

    public DotNetSolutionInfo Solution => _solution;

    public NormalizedPath ProjectFileSubPath => _projectFileSubPath;

    public bool IsPackable => _isPackable;

    public override string ToString() => $"{_solution.RawSolution.Name}/{_projectFileSubPath.LastPart}";
}
