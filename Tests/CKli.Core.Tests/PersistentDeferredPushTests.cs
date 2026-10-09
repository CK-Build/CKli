using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// <see cref="GitRepository.PersistentDeferredPushRefSpecs"/> survive the process: they are pushed by the next
/// successful push, whatever pushes and whenever it happens.
/// </summary>
[TestFixture]
public class PersistentDeferredPushTests
{
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [Test]
    public void rebuilt_tag_is_pushed_by_the_next_process()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "One" );
        var remoteUrl = remotes.GetUriFor( "OneRepo" );

        var timPath = context.CurrentDirectory.AppendPart( "Tim" );
        var deferredFile = timPath.Combine( ".git/CKLI_DEFERRED_PUSH" );
        string tipSha;
        using( var tim = GitRepository.Clone( TestHelper.Monitor,
                                              new GitRepositoryKey( context.SecretsStore, remoteUrl, true ),
                                              context.Committer,
                                              timPath,
                                              timPath.LastPart ).ShouldNotBeNull() )
        {
            // The remote has a lightweight tag.
            tipSha = tim.Repository.Head.Tip.Sha;
            tim.Repository.ApplyTag( "v1.0.0", tipSha );
            tim.PushTags( TestHelper.Monitor, ["v1.0.0"] ).ShouldBeTrue();

            // It is locally "rebuilt" as an annotated one.
            tim.Repository.Tags.Remove( "v1.0.0" );
            tim.Repository.ApplyTag( "v1.0.0", tipSha, context.Committer, "Rebuilt." );
            tim.AddPersistentDeferredPushRefSpecs( TestHelper.Monitor, ["+refs/tags/v1.0.0"] ).ShouldBeTrue();
            File.Exists( deferredFile ).ShouldBeTrue();

            // A refused ref spec is an error and nothing is added.
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                tim.AddPersistentDeferredPushRefSpecs( TestHelper.Monitor, ["+refs/tags/v2.0.0", "+refs/tags/local/v2.0.0"] ).ShouldBeFalse();
                logs.ShouldContain( l => l.Contains( "cannot be pushed" ) );
            }
            tim.PersistentDeferredPushRefSpecs.ShouldBe( ["+refs/tags/v1.0.0"] );
            // The process ends here without any push.
        }

        using( var tim = GitRepository.Open( TestHelper.Monitor,
                                             context.SecretsStore,
                                             context.Committer,
                                             timPath,
                                             timPath.LastPart,
                                             isPublic: true ).ShouldNotBeNull() )
        {
            tim.DeferredPushRefSpecs.ShouldBeEmpty();
            tim.PersistentDeferredPushRefSpecs.ShouldBe( ["+refs/tags/v1.0.0"] );

            tim.GetRemoteTags( TestHelper.Monitor, out var remoteTags ).ShouldBeTrue();
            remoteTags.IndexedTags["refs/tags/v1.0.0"].Annotation.ShouldBeNull( "Still the lightweight one." );

            // Removing a tag that is not on the remote is not an error.
            tim.AddPersistentDeferredPushRefSpecs( TestHelper.Monitor, [":refs/tags/never-pushed"] ).ShouldBeTrue();

            // An empty PushTags pushes the pending ones.
            tim.PushTags( TestHelper.Monitor, [] ).ShouldBeTrue();

            tim.PersistentDeferredPushRefSpecs.ShouldBeEmpty();
            File.Exists( deferredFile ).ShouldBeFalse();
            tim.GetRemoteTags( TestHelper.Monitor, out remoteTags ).ShouldBeTrue();
            remoteTags.IndexedTags["refs/tags/v1.0.0"].Annotation.ShouldNotBeNull( "The rebuilt annotated tag has been pushed." );

            // Any push flushes them: here, the push of a branch.
            tim.Repository.ApplyTag( "v2.0.0", tipSha, context.Committer, "Another one." );
            tim.AddPersistentDeferredPushRefSpecs( TestHelper.Monitor, ["+refs/tags/v2.0.0"] ).ShouldBeTrue();
            var b = tim.EnsureBranch( TestHelper.Monitor, "test-1" ).ShouldNotBeNull();
            tim.PushBranch( TestHelper.Monitor, b, autoCreateRemoteBranch: true ).ShouldBeTrue();

            tim.PersistentDeferredPushRefSpecs.ShouldBeEmpty();
            File.Exists( deferredFile ).ShouldBeFalse();
            tim.GetRemoteTags( TestHelper.Monitor, out remoteTags ).ShouldBeTrue();
            remoteTags.IndexedTags.ContainsKey( "refs/tags/v2.0.0" ).ShouldBeTrue();
        }
    }
}
