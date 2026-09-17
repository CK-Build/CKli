using CK.Core;
using CK.Monitoring;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// "ckli push" pulls and then pushes the Repos in parallel (repositories are independent) and pushes each
/// of them with a single network operation: all its branches that track an "origin" branch at once.
/// <para>
/// These tests cover the Repos' loop of the command. The Stack repository itself is pushed alone, before
/// the Repos (see <see cref="StackRepository.PushChanges(IActivityMonitor, bool)"/>).
/// </para>
/// <para>
/// The "WithIssues" remotes are used because they are the only ones with more than one repository. Their
/// issues come from the VSSolutionSample plugin (see <see cref="PluginTests"/>): without it, this World is
/// a plain 3 repositories World.
/// </para>
/// </summary>
[TestFixture]
public class PushTests
{
    // "ckli push" pushes to the file:// remotes: the write PAT is required.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    /// <summary>
    /// The unbounded and the sequential (--max-dop 1) paths must obviously produce the same remotes.
    /// </summary>
    [TestCase( null )]
    [TestCase( "1" )]
    public async Task push_pushes_the_tracking_branches_of_every_repository_Async( string? maxDop )
    {
        var context = TestEnv.EnsureCleanFolder( $"push_all_{maxDop ?? "parallel"}" );
        var remotes = TestEnv.OpenRemotes( "WithIssues" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        var inStack = context.ChangeDirectory( "WithIssues" );

        // Arrange: a new commit on the checked out branch of every repository.
        // Keys are the repository folder names: they are the remote names.
        var expected = new Dictionary<string, (string Branch, string Sha)>();
        {
            var (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor,
                                                                       inStack,
                                                                       out var error,
                                                                       skipPullStack: true,
                                                                       withPlugins: false );
            error.ShouldBeFalse();
            using( stack )
            {
                var repos = world.ShouldNotBeNull().GetAllDefinedRepo( TestHelper.Monitor ).ShouldNotBeNull();
                repos.Count.ShouldBeGreaterThan( 1, "This is about more than one repository." );
                foreach( var repo in repos )
                {
                    var git = repo.GitRepository;
                    File.WriteAllText( git.WorkingFolder.AppendPart( "PushTests.txt" ), repo.DisplayPath );
                    git.Commit( TestHelper.Monitor, "Testing 'ckli push'." ).ShouldBe( CommitResult.Committed );
                    expected.Add( git.WorkingFolder.LastPart, (git.CurrentBranchName, git.Repository.Head.Tip.Sha) );
                }
            }
        }
        // The remotes don't have the commits yet.
        foreach( var (name, e) in expected )
        {
            GetRemoteBranchTipSha( remotes, name, e.Branch ).ShouldNotBe( e.Sha );
        }

        (await Exec( inStack, maxDop )).ShouldBeTrue();

        foreach( var (name, e) in expected )
        {
            GetRemoteBranchTipSha( remotes, name, e.Branch )
                .ShouldBe( e.Sha, $"Branch '{e.Branch}' of '{name}' has been pushed." );
        }
    }

    /// <summary>
    /// A "local/" or "building/" branch is skipped with a warning (it is not an error here: the user doesn't
    /// name the branches). See <see cref="PushLocalRefTests"/>.
    /// </summary>
    [Test]
    public async Task push_skips_the_local_and_building_branches_with_a_warning_Async()
    {
        const string repositoryName = "EmptySolution";
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "WithIssues" );

        // A "local/" branch is considered by "ckli push" only if it tracks an "origin" branch. The pull that
        // "ckli push" always does first prunes the remote tracking references, so the remote must really have
        // this branch: this is the state that a remote polluted before the push filter existed looks like.
        // OpenRemotes re-extracts the bare repository on every call: this cannot leak into another test.
        var remoteSha = AddRemoteBranch( remotes, repositoryName, "local/xxx" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        var inStack = context.ChangeDirectory( "WithIssues" );

        // Arrange: the local branch (the "Automatic Remote Origin Branch Association Strategy" of the pull
        // makes it track "origin/local/xxx") with a commit that the remote doesn't have.
        {
            var (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor,
                                                                       inStack,
                                                                       out var error,
                                                                       skipPullStack: true,
                                                                       withPlugins: false );
            error.ShouldBeFalse();
            using( stack )
            {
                var repos = world.ShouldNotBeNull().GetAllDefinedRepo( TestHelper.Monitor ).ShouldNotBeNull();
                var git = repos.Single( r => r.WorkingFolder.LastPart == repositoryName ).GitRepository;
                var b = git.EnsureBranch( TestHelper.Monitor, "local/xxx" ).ShouldNotBeNull();
                git.Checkout( TestHelper.Monitor, b ).ShouldBeTrue();
                File.WriteAllText( git.WorkingFolder.AppendPart( "PushTests.txt" ), "Must not be pushed." );
                git.Commit( TestHelper.Monitor, "On a 'local/' branch." ).ShouldBe( CommitResult.Committed );
            }
        }

        // A GrandOutput memory collector, not TestHelper.Monitor.CollectTexts: the repositories are pushed
        // on the ActivityMonitorAsyncPool's own monitors, which a monitor scoped client never sees.
        using( var logs = GrandOutput.Default!.CreateMemoryCollector( 1000 ) )
        {
            (await Exec( inStack, maxDop: null )).ShouldBeTrue( "Skipped, not failed: the user doesn't name the branches here." );
            logs.ExtractCurrentTexts()
                .ShouldContain( t => t.Contains( "Skipping branch 'local/xxx'" )
                                     && t.Contains( "are never pushed" ) );
        }
        GetRemoteBranchTipSha( remotes, repositoryName, "local/xxx" )
            .ShouldBe( remoteSha, "The remote branch has not moved." );
    }

    static string AddRemoteBranch( TestEnv.RemotesCollection remotes, string repositoryName, string branchName )
    {
        using var bare = new Repository( remotes.GetUriFor( repositoryName ).LocalPath );
        var sha = bare.Head.Tip.Sha;
        bare.Refs.Add( $"refs/heads/{branchName}", sha );
        return sha;
    }

    static ValueTask<bool> Exec( CKliEnv inStack, string? maxDop )
    {
        return maxDop == null
                ? CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "push" )
                : CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "push", "--max-dop", maxDop );
    }

    static string? GetRemoteBranchTipSha( TestEnv.RemotesCollection remotes, string repositoryName, string branchName )
    {
        using var bare = new Repository( remotes.GetUriFor( repositoryName ).LocalPath );
        return bare.Branches[branchName]?.Tip?.Sha;
    }
}
