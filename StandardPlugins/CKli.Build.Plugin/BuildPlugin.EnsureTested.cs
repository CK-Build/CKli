using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    static TestFunction _testFunction = RealTestAsync;

    /// <summary>
    /// Sets the test function that <see cref="EnsureTestedAsync"/> uses.
    /// <para>
    /// This is mainly for tests, to inject a fake one (along with <see cref="SetBuilderFunction(BuilderFunction?)"/>).
    /// </para>
    /// </summary>
    /// <param name="test">The function or null to use the default implementation.</param>
    /// <returns>The previous test function.</returns>
    public static TestFunction SetTestFunction( TestFunction? test )
    {
        var previous = _testFunction;
        _testFunction = test ?? RealTestAsync;
        return previous;
    }

    /// <summary>
    /// A release whose tests must have run before it is published: the version tagged on <see cref="Commit"/>.
    /// </summary>
    /// <param name="Repo">The repository.</param>
    /// <param name="Version">The released version.</param>
    /// <param name="Commit">The commit that bears the version.</param>
    public readonly record struct ReleaseCommit( Repo Repo, SVersion Version, Commit Commit );

    /// <summary>
    /// Gets the releases whose content has never been successfully tested (see <see cref="RepoBuilder.HasTestRun"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="releases">The releases to check.</param>
    /// <returns>The untested releases.</returns>
    public IReadOnlyList<ReleaseCommit> GetUntestedReleases( IActivityMonitor monitor, IEnumerable<ReleaseCommit> releases )
    {
        return releases.Where( r => !_repoBuilder.Get( monitor, r.Repo ).HasTestRun( monitor, r.Commit ) ).ToList();
    }

    /// <summary>
    /// Ensures that the tests of already built releases have successfully run on their content: the publication
    /// calls this before publishing anything, since a release built with "--skip-tests" must not be published
    /// untested.
    /// <para>
    /// Each release to test is checked out, built and tested (no package, no asset, no tag: its artifacts exist
    /// and are not touched) and its tested content is recorded. The releases of a repository are tested one after
    /// the other, the repositories in parallel (up to <paramref name="maxDop"/>).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The CKli context.</param>
    /// <param name="releases">The releases to publish.</param>
    /// <param name="forceTests">True to run the tests even if they already ran successfully on a release's content.</param>
    /// <param name="maxDop">The maximal number of repositories tested concurrently.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>True if all the tests passed, false otherwise.</returns>
    public async Task<bool> EnsureTestedAsync( IActivityMonitor monitor,
                                               CKliEnv context,
                                               IEnumerable<ReleaseCommit> releases,
                                               bool forceTests,
                                               int maxDop,
                                               CancellationToken cancellation )
    {
        Throw.CheckArgument( maxDop >= 1 );
        // The same content is tested once.
        var toTest = (forceTests ? releases.ToList() : GetUntestedReleases( monitor, releases ))
                        .DistinctBy( r => (r.Repo, r.Commit.Tree.Sha) )
                        .ToList();
        if( toTest.Count == 0 ) return true;
        using var _ = monitor.OpenInfo( ScreenType.ScreenTag,
                                        forceTests
                                          ? $"Running the tests of the {toTest.Count} release(s) to publish (--force-tests)."
                                          : $"Running the tests of {toTest.Count} release(s) to publish that have never been tested." );
        var pool = new ActivityMonitorAsyncPool( maxDop );
        var monitorLock = new Lock();
        var tasks = toTest.GroupBy( r => r.Repo )
                          .Select( g => Task.Run( () => TestRepoAsync( g ) ) )
                          .ToArray();
        var results = await Task.WhenAll( tasks ).ConfigureAwait( false );
        return results.All( Util.FuncIdentity );

        async Task<bool> TestRepoAsync( IEnumerable<ReleaseCommit> repoReleases )
        {
            using var m = await pool.GetAsync( cancellation ).ConfigureAwait( false );
            if( m == null ) return false;
            foreach( var r in repoReleases )
            {
                bool success;
                IReadOnlyList<ActivityMonitorSimpleCollector.Entry> entries;
                using( m.CollectEntries( out entries, LogLevelFilter.Warn ) )
                {
                    try
                    {
                        success = await TestReleaseAsync( m, context, r, cancellation ).ConfigureAwait( false );
                    }
                    catch( Exception ex )
                    {
                        m.Error( $"While testing '{r.Repo.DisplayPath}/v{r.Version}'.", ex );
                        success = false;
                    }
                }
                // The pooled monitors are not on the screen: what matters is relayed to the command's monitor.
                lock( monitorLock )
                {
                    foreach( var e in entries )
                    {
                        monitor.Log( e.MaskedLevel, e.Tags, e.Text, e.Exception );
                    }
                    if( success )
                    {
                        monitor.Info( ScreenType.ScreenTag, $"Tests of '{r.Repo.DisplayPath}/v{r.Version}' passed." );
                    }
                    else if( !cancellation.IsCancellationRequested )
                    {
                        monitor.Error( $"Tests of '{r.Repo.DisplayPath}/v{r.Version}' failed: it cannot be published." );
                    }
                }
                if( !success ) return false;
            }
            return true;
        }
    }

    // Checks out the release commit, calls the TestFunction, records the tested content, resets the working folder
    // and restores the head. As for CheckedOutBuildAsync, everything here is what every test run must do: the
    // TestFunction (that a test harness replaces) only builds and tests.
    async Task<bool> TestReleaseAsync( IActivityMonitor monitor, CKliEnv context, ReleaseCommit release, CancellationToken cancellation )
    {
        var repo = release.Repo;
        using var gLog = monitor.OpenTrace( $"Testing '{repo.DisplayPath}/v{release.Version}' on commit '{release.Commit.Sha}'." );
        if( cancellation.IsCancellationRequested ) return false;

        var versionInfo = _versionTag.Get( monitor, repo );
        var repoBuilder = _repoBuilder.Get( monitor, repo );
        // The release already exists on this commit: this is the build info that produced it.
        var buildInfo = versionInfo.TryGetCommitBuildInfo( monitor, release.Commit, release.Version, allowRebuildVersion: true );
        if( buildInfo == null ) return false;

        var git = repo.GitRepository;
        Branch currentHead = git.Repository.Head;
        bool mustCheckOut = currentHead.Tip.Tree.Sha != release.Commit.Tree.Sha;
        if( mustCheckOut )
        {
            if( !git.Checkout( monitor, release.Commit ) ) return false;
        }
        else if( !git.CheckCleanCommit( monitor ) )
        {
            return false;
        }
        try
        {
            if( !_repoBuilder.DeleteBeforeBuild( monitor, repo ) ) return false;
            if( !await _testFunction( monitor, context, versionInfo, release.Commit, repoBuilder, buildInfo, cancellation ).ConfigureAwait( false )
                || cancellation.IsCancellationRequested )
            {
                return false;
            }
            // The release cannot change anymore: what the tests changed is only reported (and reset below).
            var impacts = GetBuildImpacts( git );
            if( impacts.Count > 0 )
            {
                monitor.Warn( $"""
                    The tests of '{buildInfo}' changed {impacts.Count} files, they are discarded (the release cannot change):
                    {impacts.Concatenate( Environment.NewLine )}
                    """ );
            }
            repoBuilder.SetTestRun( monitor, release.Commit );
            return true;
        }
        finally
        {
            git.ResetHard( monitor, out var _ );
            FileHelper.DeleteEmptyFoldersBelow( monitor, repo.WorkingFolder, CK.Core.LogLevel.Warn );
            if( mustCheckOut )
            {
                monitor.Trace( "Restoring working folder to its previous head." );
                git.Checkout( monitor, currentHead, deleteIgnored: true );
            }
        }
    }

    static Task<bool> RealTestAsync( IActivityMonitor monitor,
                                     CKliEnv context,
                                     VersionTagInfo versionInfo,
                                     Commit testCommit,
                                     RepoBuilder repoBuilder,
                                     CommitBuildInfo buildInfo,
                                     CancellationToken cancellation )
    {
        return repoBuilder.TestAsync( monitor, context, buildInfo, cancellation );
    }
}
