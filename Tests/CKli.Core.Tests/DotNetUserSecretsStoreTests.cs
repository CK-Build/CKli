using CK.Core;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class DotNetUserSecretsStoreTests
{
    /// <summary>
    /// A test that needs a secret registers it at its start (and clears it afterwards): the store must see
    /// a file that changed since it has been read, otherwise a test would be reading the store from before
    /// its own setup. This only holds when <see cref="CKliRootEnv.IsTestRun"/> is true.
    /// </summary>
    [Test]
    public void store_sees_a_secret_registered_after_it_has_been_read()
    {
        CKliRootEnv.IsTestRun.ShouldBeTrue( "Otherwise the store never reloads." );
        TestEnv.RemoveFileSystemWritePAT();
        using var store = new DotNetUserSecretsStore();
        try
        {
            // This first read caches a document without the secret.
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                store.TryGetRequiredSecret( TestHelper.Monitor, "FILESYSTEM_GIT" ).ShouldBeNull();
                logs.ShouldContain( l => l.Contains( "This operation requires the secret 'FILESYSTEM_GIT'." ) );
            }

            TestEnv.SetFileSystemWritePAT();
            store.TryGetRequiredSecret( TestHelper.Monitor, "FILESYSTEM_GIT" ).ShouldBe( "don't care" );

            // Removing it must be seen as well: a test must not benefit from another one's secret.
            TestEnv.RemoveFileSystemWritePAT();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                store.TryGetRequiredSecret( TestHelper.Monitor, "FILESYSTEM_GIT" ).ShouldBeNull();
            }
        }
        finally
        {
            TestEnv.RemoveFileSystemWritePAT();
        }
    }
}
