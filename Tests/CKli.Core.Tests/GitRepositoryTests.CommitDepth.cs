using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// The commit depth (the CI number of a CI version) is the longest path from the base commit.
/// </summary>
public partial class GitRepositoryTests
{
    /// <summary>
    /// <code>
    ///   B -- a1 ---------- M -- C
    ///    \                /
    ///     b1 -- b2 ------
    /// </code>
    /// The second parent of M is one commit deeper than the first: M is at depth 3 and its child at depth 4. The depth
    /// was 2 for M (the depth of b2) and 3 for C: a version tagged on M had the number of its child (CK World, 2026-11-12).
    /// </summary>
    [Test]
    public void the_commit_depth_is_the_longest_path_to_the_base()
    {
        var context = TestEnv.EnsureCleanFolder();
        using var r = CreateRepositoryWithCommit( context );
        var git = r.Repository;
        var b = git.Head.Tip;
        var a1 = Child( git, "a1", b );
        var b1 = Child( git, "b1", b );
        var b2 = Child( git, "b2", b1 );
        var m = Child( git, "M", a1, b2 );
        var c = Child( git, "C", m );

        r.ComputeCommitDepth( TestHelper.Monitor, b, b ).ShouldBe( 0 );
        r.ComputeCommitDepth( TestHelper.Monitor, b, b2 ).ShouldBe( 2 );
        r.ComputeCommitDepth( TestHelper.Monitor, b, m ).ShouldBe( 3 );
        r.ComputeCommitDepth( TestHelper.Monitor, b, c ).ShouldBe( 4 );
        // The order of the parents doesn't matter.
        r.ComputeCommitDepth( TestHelper.Monitor, b, Child( git, "M'", b2, a1 ) ).ShouldBe( 3 );
        // A parent that doesn't reach the base is ignored.
        var unrelated = git.ObjectDatabase.CreateCommit( Signature(), Signature(), "Unrelated.", b.Tree, [], prettifyMessage: false );
        r.ComputeCommitDepth( TestHelper.Monitor, b, Child( git, "M''", unrelated, a1 ) ).ShouldBe( 2 );
        r.ComputeCommitDepth( TestHelper.Monitor, b, unrelated ).ShouldBe( -1 );

        static Commit Child( Repository git, string message, params Commit[] parents )
        {
            return git.ObjectDatabase.CreateCommit( Signature(), Signature(), message, parents[0].Tree, parents, prettifyMessage: false );
        }

        static Signature Signature() => new Signature( "CKli.Tests", "none", DateTimeOffset.Now );
    }
}
