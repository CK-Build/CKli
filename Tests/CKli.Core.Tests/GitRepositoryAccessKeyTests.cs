using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

/// <summary>
/// Access keys are interned in a process wide cache and resolve their credentials lazily from the store
/// they were created with. A parallel fetch resolves the key of every repository it fetches at once, so
/// both are concurrent surfaces.
/// <para>
/// Before they were synchronized, "ckli deps update" - which fetches the whole World through an
/// <see cref="ActivityMonitorAsyncPool"/> - failed on every repository with either "An item with the same
/// key has already been added" or, once the dictionary was actually corrupted, "Operations that change
/// non-concurrent collections must have exclusive access".
/// </para>
/// </summary>
[TestFixture]
public class GitRepositoryAccessKeyTests
{
    // The World that surfaced this has 18 repositories, all under one owner: they share a single PrefixPAT
    // and therefore a single cache entry, which is what makes the race certain rather than unlikely.
    const int _repositoryCount = 18;

    [Test]
    public void access_keys_are_resolved_concurrently()
    {
        // The store is part of the cache key, so a fresh one gives this test its own key space.
        var secretsStore = new RecordingSecretsStore();
        var urls = Enumerable.Range( 0, _repositoryCount )
                             .Select( i => new Uri( $"https://github.com/ACME/Repo-{i}" ) )
                             .ToArray();

        var keys = new IGitRepositoryAccessKey[urls.Length];
        Parallel.For( 0, urls.Length, i => keys[i] = new GitRepositoryKey( secretsStore, urls[i], isPublic: false ).AccessKey );

        keys.ShouldAllBe( k => k.PrefixPAT == "GITHUB_ACME" );
        // One owner and one store: whoever won the race, all the repositories share the one instance.
        keys.Distinct().Count().ShouldBe( 1 );
    }

    [Test]
    public void credentials_are_resolved_concurrently()
    {
        var secretsStore = new RecordingSecretsStore();
        var keys = Enumerable.Range( 0, _repositoryCount )
                             .Select( i => new GitRepositoryKey( secretsStore,
                                                                 new Uri( $"https://github.com/CONTOSO/Repo-{i}" ),
                                                                 isPublic: false ).AccessKey )
                             .ToArray();

        // A monitor is not thread safe: the pool gives each of its workers one, so must this.
        Parallel.ForEach( keys, k => k.GetReadCredentials( new ActivityMonitor(), out _ ).ShouldBeFalse() );

        // The store has no secret, so nothing is ever cached and each call reaches it. Its own state is not
        // concurrent either: this count is short by at least one as soon as two threads enter it together.
        secretsStore.RequestedKeys.Count.ShouldBe( keys.Length );
    }
}
