using CSemVer;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;

namespace CKli.Core.Tests;

[TestFixture]
public class Branch4Tests
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
        // To limit the complexity, we sould consider that the initial "command" applies:
        // "simply build this repo and propagate" triggers a simple build of the downstream's dependencies (don't
        // try to produce new, unrelated, packages that can be produced by upstreams of downstreams, just upgrade to the existing ones)
        // whereas "fully build this repo and propagate" triggers the production of new packages.
        //
        // One can replace "simple", "full" and "propagate" with:
        // - build: upgrade, compile, test, package
        // - *build: recursive downstream build.
        // - build*: recursive upstream build and propagation.
        // - *build*: recusively combines *build and build*.
        //
        // What does "upgrade" means? Whether the build is recursive or not, we end up with a set of dependencies
        // whose versions must be updated in the Repo's projects (.csproj or Directory.Packages.props). This is obvious.
        // But the Branch4 model introduces another kind of dependency: the fact that "alpha" is based on "beta" which is based on "preview" which is
        // based on "rc" which is ultimately based on "stable".
        // Upgrading the Repo A in "alpha" may also means that if new stuff appeared in "rc", "preview" or "beta", then
        // this must appear in the new "alpha"...
        // Technically, this should be a Git rebase on the base branch (but this can also be done with a merge).
        // Is this "too strong"? May be. We may need some option to keep a branch independent of its base but eventually
        // all the work will be merged into a future stable. The sooner the better can be a not so bad approach.
        // We should at least consider rebasing on the last produced commits of the base branches: this would be the
        // "build". And with "*build", we rebase on the tip of the base branch.
        //
        // These very simple workflows requires a rather strong condition: we must be able to:
        //  - Decide whether new packages must be produced for a repo (or to use the last produced packages).
        //  - Automatically produce a version number for the new packages.
        // On prerealease branches, the version number can simply be incremented. But on "stable" we have to know
        // if the release is a Breacking (Major+1), Feature (Minor+1) or Fix (Patch+1). This may be generalized
        // if we consider that we always build a branch with Breacking/Feature/Fix indicator.
        // The Major.Minor.Patch version of any new version cannot be greater than the (Major+1,0,0), (Major,Minor+1,0)
        // or (Major,Minor,Patch+1) of the greater stable existing version.
        //
        // Thanks to the CSemVer "Post Release" feature, the "current" repository version (the greatest Major.Minor.Patch
        // that can be found in all versio tags) de facto memorizes the future stable version number.
        //
        // The greatest stable version can easily be determined for any Repo.
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
        // The build worflow is the same as the current, basic, workflow "master/develop". Developments occur in the "-dev" branch,
        // commits produce ci artifacts and "release" is made by merging the "XXX-dev" branch into "XXX". 
        //
        // The CKli.Build.Plugin is able to compute the version numbers and dosn't use SimpleGitVersion. It uses CSemVer for its
        // SVersion and the new CSVersion4 that encapsulates the Branch4 model versions.
        // Not using SimpleGitVersion means that a remote CI can no more build/test/package a commit point.
        // This tool will be developed later.
        //
        // ckli build           Current Repo must be on the "stable", "stable-dev", "rc", "rc-dev", "pre", "pre-dev", "beta",
        //                      "beta-dev", "alpha" or "alpha-dev" branch.
        //                      If not it is an error.
        //
        // Steps:
        //  0 - If the --branch is specified, set the current branch (the current Repo must not be dirty otherise it is an error).
        //  1 - Branch rebase:
        //      - If on a "-dev" branch, it is a CI build:
        //          - If on "stable-dev", there is no branch rebase to do.
        //          - Otherwise, different rebase strategies exist:
        //              - none: no branch rebase.
        //              - full-dev: the branch is rebased on all the "-dev" branches of its base branches.
        //              - full: the branch is rebased on all its base branches at their current tip.
        //              - released: the branch is rebased on the most recent commit with a version tag of all its base branches.
        //      - If on a non "-dev" branch
        //          - If on "stable", there is no branch rebase to do.
        //          - Otherwise, the rebase strategy can be:
        //              - full: the branch is rebased on all its base branches at their current tip.
        //              - released: the branch is rebased on the most recent commit with a version tag of all its base branches.
        //              There is no "none" here: when releasing an alpha version we want it to integrate the current "rc", "pre"
        //              and "beta" code base.
        //  2 - The package references are collected.
        //  3 - Projects package references to Stack's Repos (these are the upstream Repos).
        //  4 - For each upstream Repo, obtain the last produced version in the preference order of the base branches,
        //      accounting .ci packages (if on a "-dev" branch).
        //              There is an issue here regarding missing base branches.
        //                 - A "1.0.0-01-01-01-01-alpha" has been produced
        //                 - Later, the "rc" branch is deleted because it has been integrated into the "stable-dev" branch.
        //                 - A second alpha appears: its number MUST NOT be "1.0.0-00-01-01-02-alpha" but "1.0.0-01-01-01-02-alpha"
        //                   (the rc 01 slot must be kept).
        //                   This will be discussed in the Branch Management section below.
        // 5 - Update the package references in the current Repo.
        // 6 - Compute the base version V = (VMajor,VMinor,VPatch):
        //      - When --version is not specified, "patch" is assumed.
        //      - Considering the last stable version tag S = (SMajor,SMinor,SPatch) and the
        //        greatest version tag (including prereleases) G = (GMajor,GMinor,GPatch).
        //      - If GMajor < SMajor or GMajor > SMajor + 1 or GMinor > SMinor + 1 or GPatch > SPatch + 1
        //        this is an error.
        //      - If GMajor == SMajor + 1
        //          - GMinor and GPatch must be 0 otherwise it is an error.
        //          - V = (GMajor,0,0)
        //        else (GMajor == SMajor)
        //          - If GMinor < SMinor this is an error.
        //          - If GMinor == SMinor + 1
        //              - If GPatch > 0 this is an error.
        //                else if GPatch < SPatch this is an error.
        //          - If --version is "major", V = (SMajor+1,0,0)
        //            else, search for the string "[Breaking]" (and "[Feature]" if --version is "patch") in all commit messages
        //            for commits between head and S.
        //              - If head is not a parent of S, this is an error. 
        //            If "[Breaking]" is found, V = (SMajor+1,0,0)
        //            else if --version is "minor" or "[Feature]" is found V = (SMajor,SMinor+1,0)
        //            else V = (SMajor,SMinor,SPatch+1).
        // 7 - Compute the final version VF:
        //      - If on the "stable" branch, VF = v.
        //        else compute the commit depth (up to 9999) and generate VF for the current branch (see below).
        // 8 - Run the build/test/package (with version VF).
        //
        // The "ckli *build" 
    }
}
