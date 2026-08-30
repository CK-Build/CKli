using CK.Core;
using CK.Testing;
using CKli.Core;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli;

/// <summary>
/// Models a repository working folder in a <see cref="FakeBuildWorld"/>.
/// </summary>
public sealed partial class FakeBuildRepo
{
    readonly FakeBuildWorld _world;
    readonly IMonitorTestHelper _helper;
    readonly NormalizedPath _path;
    readonly NormalizedPath _displayPath;
    readonly string _defaultProjectName;
    CKliEnv? _repoRoot;

    internal FakeBuildRepo( FakeBuildWorld world, IMonitorTestHelper helper, NormalizedPath path )
    {
        Throw.DebugAssert( path.StartsWith( world.WorldRoot.CurrentDirectory ) );
        _world = world;
        _helper = helper;
        _path = path;
        _defaultProjectName = _path.LastPart.Replace( '-', '.' );
        _displayPath = path.RemoveParts( 0, world.WorldRoot.CurrentDirectory.Parts.Count );
    }

    /// <summary>
    /// Gets the world to which this Repo belongs.
    /// </summary>
    public FakeBuildWorld World => _world;

    /// <summary>
    /// Gets the Repo working folder full path.
    /// </summary>
    public NormalizedPath WorkingFolderPath => _path;

    /// <summary>
    /// Gets the relative path of this repo in the <see cref="World"/>.
    /// </summary>
    public NormalizedPath DisplayPath => _displayPath;

    /// <summary>
    /// Gets the repository name.
    /// </summary>
    public string RepositoryName => _path.LastPart;

    /// <summary>
    /// Gets the default project name based on the <see cref="RepositoryName"/>.
    /// </summary>
    public string DefaultProjectName => _defaultProjectName;

    /// <summary>
    /// Gets a context for this repository.
    /// </summary>
    public CKliEnv? RepoRoot => _repoRoot ??= _world.WorldRoot.ChangeDirectory( _path );

    /// <summary>
    /// Creates a temporary editor for the repository.
    /// </summary>
    /// <returns>A temporary editor.</returns>
    public Editor CreateEditor() => new Editor( this, _helper );
}
