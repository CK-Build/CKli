using CSemVer;
using NUnit.Framework;
using Shouldly;
using System.Linq;

namespace CKli.Core.Tests;

[TestFixture]
public class Branch4PackageTests
{
    [Test]
    public void version_ordering_pattern()
    {
        /// 2 ^ 63 = 9 223 372 036 854 775 808
        /// 10000 major
        /// 3037 minor
        /// 3037 patch
        /// => alpha, beta, pre, rc (each with 100)
        /// r01-p56-b00-a03
        (long.MaxValue > 10000L * 3037L * 3037L * 100L * 100L * 100L * 100L).ShouldBeTrue();
    }

    [Test]
    public void regular_naming()
    {
        // The package version number must capture 4 pre-releases branch hierarchy.
        // The first idea looks like:
        //  var vAlpha = SVersion.Parse( "1.0.0-r01-p01-b01-a01" );
        //  var vBeta = SVersion.Parse( "1.0.0-r01-p01-b01" );
        //  var vPre = SVersion.Parse( "1.0.0-r01-p01" );
        //  var vRc = SVersion.Parse( "1.0.0-r01" );
        //  (vAlpha > vBeta).ShouldBeTrue();
        //  (vBeta > vPre).ShouldBeTrue();
        //  (vPre > vRc).ShouldBeTrue();
        //
        // "Upgrade with PreRelease" will get the latest alpha unless we filter.
        // This can work... CKli can fix this (in a "pre" branch, it uses vP).
        //
        // vP cannot exist without VR.
        // Its like a stack of "quality". When we "open the alpha channel", we must consider that "rc", "pre", and "beta"
        // branches are available.
        // If we don't want to actually create the branches, we should reserve the 00 version:
        // 1.0.0-r00 is invalid, the package doesn't exist.
        // 1.0.0-r00-p00-b00-a01 is the first alpha version of the 1.0.0 when only "stable" and "alpha"
        // branch exist.
        //
        // Do we need the r, p, b, a?
        // Not really. The pattern can be:
        //   1.0.0-01-rc
        //   1.0.0-01-01-pre
        //   1.0.0-01-01-01-beta
        //   1.0.0-01-01-01-01-alpha
        //
        // This pattern is compatible with CI builds:
        //  1.0.1--0000                First CI build of the 1.0.0 release (the 0000 develop version - in debug).
        //  1.0.1--01-0000-rc          First CI build of the 1.0.0-rc release (the 0000 develop version - in debug).
        //  1.0.1--01-0042-rc          Subsequent CI build of the 1.0.0-rc release.
        //  1.0.0--00-00-00-0001-beta  First CI build of the first beta (with no "rc" nor "pre" branch).
        //
        // We reuse the CSemVer Post-Release ("--" trick with the next-to-be-released version): this makes CI build incompatible with nuget.org
        // but this is more a feature than an issue.
        // We can add a .ci suffix to ease reading and filtering versions.
        // The "nXXXX" guaranties that 10000 ci versions can be built per CSVersion4 versions. The 'n' prefix is here because it is
        // required to order "1.0.1--n0000.ci" before the other pre release branches ci versions.
        //
        SVersion[] ordered1 =
            [
                SVersion.Parse( "1.0.1" ),
                SVersion.Parse( "1.0.1-00-01-pre" ),
                SVersion.Parse( "1.0.1--n0000.ci" ),
                SVersion.Parse( "1.0.1--01-rc-n0042.ci" ),
                SVersion.Parse( "1.0.1--01-rc-n0000.ci" ),
                SVersion.Parse( "1.0.1--01-01-pre-n0001.ci" ),
                SVersion.Parse( "1.0.1--01-01-00-01-alpha-n0001.ci" ),
                SVersion.Parse( "1.0.0" ),
                SVersion.Parse( "1.0.0-01-rc" ),
                SVersion.Parse( "1.0.0-01-01-pre" ),
                SVersion.Parse( "1.0.0-01-01-01-beta" ),
                SVersion.Parse( "1.0.0-01-01-00-01-alpha" )
            ];
        var o1 = ordered1.OrderDescending().ToArray();
        ordered1.ShouldBeInOrder( SortDirection.Descending );

        // Is this "channel" global to the stack?
        // "ckli branch set beta"
        // "ckli build --all"
        //
        // A Repo that has no "beta" must stay on the "stable". But if an upstream repo has
        // a "rc" branch, it must exist. But the world's topology depends on the branch ("alpha" may not have
        // the same dependencies as "rc").
        //
        // Thinking in terms of "updating dependencies" is "procedural". It may be better to think
        // more declaratively: "simply build this repo" (upgrade the dependencies to existing ones and build),
        // "fully build this repo" (recursively build the dependencies, upgrade and build).
        // If I add the words "and propagate", once built, the produced packages must be upgraded in downstream repos
        // in the corresponding branch (that must be created if it doesn't exist), downstream repos must be built
        // and recursively propagates their produced packages.
        // Should the build of downstream repos be a "simple build" or a "full build"?
        // To limit the complexity, we should consider that the initial "command" applies:
        // "simply build this repo and propagate" triggers a simple build of the downstream's dependencies (don't
        // try to produce new, unrelated, packages that can be produced by upstreams of downstreams, just upgrade to the existing ones)
        // whereas "fully build this repo and propagate" triggers the production of new packages.
        //
        // One can replace "simple", "full" and "propagate" with:
        // - build: upgrade, compile, test, package
        // - *build: recursive downstream build.
        // - build*: recursive upstream build and propagation.
        // - *build*: recursively combines *build and build*.
        //
        // What does "upgrade" means? Whether the build is recursive or not, we end up with a set of dependencies
        // whose versions must be updated in the Repo's projects (.csproj or Directory.Packages.props). This is obvious.
        // But the Branch4Package model introduces another kind of dependency: the fact that "alpha" is based on "beta" which is
        // based on "preview" which is based on "rc" which is ultimately based on "stable".
        // Upgrading the Repo A in "alpha" may also means that if new stuff appeared in "rc", "preview" or "beta", then
        // this must appear in the new "alpha"...
        // Technically, this should be a Git rebase on the base branch (but this can also be done with a merge).
        // Is this "too strong"? May be. We may need some option to keep a branch independent of its base but eventually
        // all the work will be merged into a future stable. The sooner the better can be a not so bad approach.
        // We should at least consider rebasing on the last released commits of the base branches.
        //
        // These very simple workflows requires a rather strong condition: we must be able to:
        //  - Decide whether new packages must be produced for a repo (or to use the last produced packages).
        //  - Automatically produce a version number for the new packages.
        // On prerealease branches, the version number can simply be incremented. But on "stable" we have to know
        // if the release is a Breaking (Major+1), Feature (Minor+1) or Fix (Patch+1). This may be generalized
        // if we consider that we always build a branch with Breaking/Feature/Fix indicator.
        // The Major.Minor.Patch version of any new version cannot be greater than the (Major+1,0,0), (Major,Minor+1,0)
        // or (Major,Minor,Patch+1) of the greater stable existing version.
        //
        // Thanks to the CSemVer "Post Release" feature, the "current" repository version (the greatest Major.Minor.Patch
        // that can be found in all version tags) de facto memorizes the future stable version number.
        //
        // An issue that a plugin should implement is that one cannot find an existing artifact in the feeds with the
        // greatest non prerelease version tag of a Repo. More generally, one should be able to have an issue for
        // any version tag that doesn't find its artifacts. Question: BuildAlyzer can be used to produce the expected
        // artifact list. Should we cache it? Should this list appear in the RepositoryInfo.xml?

        // The whole worflow can eventually be expressed as a simple command line that runs from a Repo:
        //   - ckli build --branch <branch> --version <major, minor or patch> 
        //
        // To support ci builds, we must associate a "dev" branch to our 5 branches. We always have between 2 and 10 and an
        // even number of well-known branches in a Repo:
        //  - "stable" and "stable-dev" (or "stable/dev", or "dev/stable"?). These 2 branches always exist.
        //  - "rc" and "rc-dev"
        //  - "pre" and "pre-dev"
        //  - "beta" and "beta-dev"
        //  - "alpha" and "alpha-dev"
        //
        // Decision: we choose the "XXX/dev" pattern to prepare for future "topic-branches" that will be 0.0.0 prerelease versions
        // (with a prerelease name that is a "topic" and may be a timed-base part).
        // A topic branch will be "based" on one of our branch, the path separator better convey the subordination semantics.
        //  
        // The build worflow is the same as the current, basic, workflow "master/develop". Developments occur in the "/dev" branch,
        // commits can produce ci artifacts and a "release" is made by merging the "XXX/dev" branch into "XXX".
        // Because developers have a mental representation of a repository and its state, it is important to enforce some principles.
        // We should avoid a "XXX" branch and its "XXX/dev" to point to the same commit. Instead we should always have:
        //
        //      + [stable]
        //      |\
        //      | \
        //      |  + [stable/dev]
        //      |  |
        //
        // Fundamental invariant here, this is the "released state" of a branch: at this point, the 2 commits must contain exactly
        // the same code (the "content sha" - the sha of the merckle tree - of the 2 commits must be the same).
        // The "released state" usually has a version tag on the base branch (but this is not required).
        //
        // If a Repo has no need for a "rc", "pre", "beta" or "alpha" branches, these should not exist. These prerelease branches
        // should be managed by command. For instance "ckli branch ensure rc" should result in:
        //
        //      + [stable]
        //      |\
        //      | \     + [rc]
        //      |  \   /
        //      |   \ /
        //      |    + [stable/dev]
        //      |    |
        //
        // The "rc" branch is created from the "stable/dev" and an empty commit point "Starting rc branch" is made in it. But "rc"
        // must always have its associated "rc/dev". The result must actually be: 
        // 
        //      + [stable]
        //      |\
        //      | \     + [rc]
        //      |  \     \
        //      |   \     + [rc/dev]
        //      |    \   /
        //      |     \ /
        //      |      + [stable/dev]
        //      |      |
        //
        // Let's now run "ckli branch ensure pre":
        // 
        //      + [stable]
        //      |\
        //      | \     + [rc]
        //      |  \     \
        //      |   \     \     + [pre]
        //      |    \     \     \
        //      |     \     \     + [pre/dev]
        //      |      \     \   /
        //      |       \     \ /
        //      |        \     + [rc/dev]
        //      |         \   /
        //      |          \ /
        //      |           + [stable/dev]
        //      |           |
        //
        // This is how prerelease branches are created (if we now run "ckli branch ensure alpha", the "beta" and "beta/dev" appear: a
        // subordinate branch implies the existence of its base branches.
        // There are 2 ways to supress a prerelease branch: the regular case is that the branch contains good enough code and it can be
        // integrated into its base branch, the less frequent case is that we want to forget it.
        //
        // A prerelease branch can be killed/forgotten only if it has no subordinated branch ("alpha" must first be killed/forgotten
        // before "beta", etc.). This is basically a "git -D branch". 
        //
        // "Integration" is basically a merge into the base branch. The branch to integrate should be in the "released state". It may
        // be possible to skip intermediate base branch: "ckli branch integrate alpha --into rc".
        // After the integration, the branch is deleted if it has no subordinated branch or the initial pattern above is recreated (as if
        // the branch has been created).
        //
        // The CKli.Build.Plugin is able to compute the version numbers and doesn't use SimpleGitVersion. It uses CSemVer for its
        // SVersion and the new CSVersion4 that encapsulates the B4P model versions.
        // Not using SimpleGitVersion means that a remote CI can no more build/test/package a commit point.
        // This tool will be developed later.
        //
        // ckli build           Current Repo must be on the "stable", "stable/dev", "rc", "rc/dev", "pre", "pre/dev", "beta",
        //                      "beta/dev", "alpha" or "alpha/dev" branch and must not be dirty.
        //                      If not it is an error.
        //
        // Steps:
        //  0 - If the --branch is specified, checkout the branch.
        //  1 - Branch rebase:
        //      - If on a "/dev" branch, it is a CI build:
        //          - If on "stable/dev", there is no branch rebase to do.
        //          - Otherwise, different rebase strategies exist:
        //              - none: no branch rebase.
        //              - full-dev: the branch is rebased on all the "-dev" branches of its base branches.
        //              - full: the branch is rebased on all its base branches at their current tip.
        //              - released: the branch is rebased on the most recent commit with a version tag of all its base branches.
        //      - If on a non "/dev" branch
        //          - If on "stable", there is no branch rebase to do.
        //          - Otherwise, the rebase strategy can be:
        //              - full: the branch is rebased on all its base branches at their current tip.
        //              - released: the branch is rebased on the most recent commit with a version tag of all its base branches.
        //              There is no "none" here: when releasing an alpha version we want it to integrate the current "rc", "pre"
        //              and "beta" code base.
        //  2 - The package references are collected.
        //  3 - Projects package references to Stack's Repos (these are the upstream Repos).
        //  4 - For each upstream Repo, obtain the last produced version in the preference order of the base branches,
        //      (accounting .ci packages if on a "-dev" branch).
        //              There is an issue here regarding missing base branches.
        //                 - A "1.0.0-01-01-01-01-alpha" has been produced
        //                 - Later, the "rc" branch is deleted because it has been integrated into the "stable/dev" branch.
        //                 - A second alpha appears: its number MUST NOT be "1.0.0-00-01-01-02-alpha" but "1.0.0-01-01-01-02-alpha"
        //                   (the rc 01 slot must be kept).
        //                   This will be discussed in the Branch Management section below.
        // 5 - Update the package references in the current Repo and create a commit if something changed.
        // 6 - Compute the base version V = (VMajor,VMinor,VPatch):
        //      - When --version is not specified, "patch" is assumed.
        //      - Considering the last stable commit cS and its version tag S = (SMajor,SMinor,SPatch)
        //      - If head is not a parent of cS, this is an error. 
        //      - If --version is "major", V = (SMajor+1,0,0)
        //        Else, V = --version == "minor" ? (SMajor,SMinor+1,0) : (SMajor,SMinor,SPatch+1).
        //        Consider the set of commits from head to cS (not that easy):
        //          - On each commit:
        //              - If message contains the string "[Breaking]" or "[Major]" or a version tag (SMajor+1,0,0) exists,
        //                V = (SMajor+1,0,0) and we are done.
        //              - If --version is "patch" and message contains "[Feature]" or "[Minor]" or a version tag (SMajor,SMinor+1,0) exists
        //                V is updated to (SMajor,SMinor+1,0).
        // 7 - Compute the final version VF:
        //      - If on the "stable" branch, VF = V.
        //        else compute the commit depth (up to 9999) and generate VF for the current branch (see below).
        // 8 - Run the build/test/package (with version VF).
        // 9 - On success, tag the commit point (should we do this also on "-dev" branches? We may not.)
        //
        // The "ckli *build" 
    }
}
