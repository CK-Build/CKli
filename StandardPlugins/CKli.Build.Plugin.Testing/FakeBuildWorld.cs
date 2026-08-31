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
    /// <param name="references">
    /// Optional repositories that the created repository's default project references. The reference is on
    /// each one's default project (its produced package) at its <see cref="FakeBuildRepo.InitialVersion"/>,
    /// and it is set up before the initial version tag: the created repository is up to date with regard to
    /// its version, so nothing is to be built.
    /// </param>
    /// <returns>The new repository.</returns>
    public async Task<FakeBuildRepo> CreateRepoAsync( string repositoryName,
                                                      string? initialVersion,
                                                      NormalizedPath subFolder = default,
                                                      params IEnumerable<FakeBuildRepo> references )
    {
        Throw.CheckArgument( repositoryName.Contains( '-' ) && !repositoryName.Contains( '.' ) );
        var v = initialVersion == null ? null : SVersion.Parse( initialVersion );
        var repo = _repositories.FirstOrDefault( r => r.RepositoryName == repositoryName );
        if( repo != null )
        {
            Throw.InvalidOperationException( $"Repo '{repositoryName}' already exists." );
        }
        var context = _worldRoot.ChangeDirectory( subFolder );
        var display = _stack.Screen;

        var repoUrl = _stack.Remotes.GetUriFor( repositoryName, mustExist: false );
        (await CKliCommands.ExecAsync( Monitor, context, "repo", "create", repoUrl ).ConfigureAwait( false )).ShouldBeTrue();
        // Creating the Repo, a Editor is created:
        // - It adds the SolutionFileName and the DefaultProjectName.csproj project.
        // - It forwards stable onto dev/stable.
        // - If a initialVersion is provided:
        //    - It removes the v0.0.0+fake (application of the Missing initial version fix).
        //    - It sets the initialVersion tags.
        var refs = references as IReadOnlyList<FakeBuildRepo> ?? [.. references];
        foreach( var r in refs )
        {
            Throw.CheckArgument( "References must belong to this World.", r.World == this );
        }
        repo = new FakeBuildRepo( this, _helper, context.CurrentDirectory.AppendPart( repositoryName ), v, refs );
        _repositories.Add( repo );
        return repo;
    }

}
