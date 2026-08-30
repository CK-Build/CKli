using CK.Core;
using CK.Testing;
using CKli.Core;
using Shouldly;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Models a world in a <see cref="FakeBuildStack"/>.
/// </summary>
public sealed class FakeBuildWorld
{
    readonly IMonitorTestHelper _helper;
    IActivityMonitor Monitor => _helper.Monitor;
    readonly FakeBuildStack _stack;
    readonly CKliEnv _worldRoot;
    readonly List<FakeBuildRepo> _repositories;

    internal FakeBuildWorld( IMonitorTestHelper helper, FakeBuildStack stack, CKliEnv worldRoot )
    {
        _helper = helper;
        _stack = stack;
        _worldRoot = worldRoot;
        _repositories = new List<FakeBuildRepo>();
    }

    /// <summary>
    /// Gets the stack to which this world belongs.
    /// </summary>
    public FakeBuildStack Stack => _stack;

    /// <summary>
    /// Gets the context of the world root.
    /// </summary>
    public CKliEnv WorldRoot => _worldRoot;

    /// <summary>
    /// Gets the repositories.
    /// </summary>
    public IReadOnlyList<FakeBuildRepo> Repositories => _repositories;

    /// <summary>
    /// Create a new (fake) repository in the stack. The root "stable" branch is checked out.
    /// </summary>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="initialVersion">Optional initial version tag to create on the root stable branch (that is checked out).</param>
    /// <param name="subFolder">Optional sub folder in the world.</param>
    /// <returns></returns>
    public async Task<FakeBuildRepo> CreateRepoAsync( string repositoryName, SVersion? initialVersion, NormalizedPath subFolder = default )
    {
        Throw.CheckArgument( repositoryName.Contains( '-' ) && !repositoryName.Contains( '.' ) );
        var repo = _repositories.FirstOrDefault( r => r.RepositoryName == repositoryName );
        if( repo == null )
        {
            var context = _worldRoot.ChangeDirectory( subFolder );
            var display = _stack.Screen;

            var repoUrl = _stack.Remotes.GetUriFor( repositoryName, mustExist: false );
            (await CKliCommands.ExecAsync( Monitor, context, "repo", "create", repoUrl ).ConfigureAwait( false )).ShouldBeTrue();
            // Must run ckli issue twice. TODO: find a way to do this only once...
            display.Clear();
            (await CKliCommands.ExecAsync( Monitor, context, "issue", "--fix" )).ShouldBeTrue();
            display.ToString().ShouldBe(
                    """
                ❰✓❱
          
                """ );
            display.Clear();
            (await CKliCommands.ExecAsync( Monitor, context, "issue", "--fix" )).ShouldBeTrue();
            display.ToString().ShouldBe(
                    """
                ❰✓❱
          
                """ );
            display.Clear();
            (await CKliCommands.ExecAsync( Monitor, context, "issue", "--fix" )).ShouldBeTrue();
            display.ToString().ShouldBe(
                    """
                ❰✓❱
          
                """ );
            repo = new FakeBuildRepo( this, _helper, context.CurrentDirectory.AppendPart( repositoryName ), initialVersion );
            _repositories.Add( repo );
        }
        return repo;
    }

}
