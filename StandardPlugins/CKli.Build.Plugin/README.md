# CKli.Build.Plugin

**CKli.Build.Plugin** is the StandardPlugin that turns a set of Git repositories into a **build roadmap** and executes it:
it decides, repository by repository, whether a build is required, computes the version to produce, updates inter-repo
package references, runs `dotnet build`/`test`/`pack`, and publishes the resulting packages to the World's local NuGet
feed. It is the computational heart of the CKli build pipeline; it does not push anything to a remote feed or Git hosting
provider - that is [`CKli.Publish.Plugin`](../CKli.Publish.Plugin)'s job.


## Why

A Stack typically contains many repositories with cross-dependencies (a `CK.Core` change must ripple through everything
that references it). Building "the right things in the right order" by hand is error-prone: you must know which
repositories changed, which of their consumers need a new package version, in what order to build them so that each one
consumes the freshly-built version of its dependencies, and whether tests already passed on a given commit so they don't
need to be re-run.

`CKli.Build.Plugin` automates this:

- It reuses the dependency graph computed by [`CKli.HotZone.Plugin`](../CKli.HotZone.Plugin) (the `HotGraph`) and layers a
  **`Roadmap`** on top of it: for every solution in the graph it decides *whether* it must be built, *why*, and *what
  version* it must produce.
- It drives the actual build (`dotnet build`/`test`/`pack`) through a per-Repo **`RepoBuilder`**, coordinates parallel
  builds with a bounded degree of parallelism, and updates each repository's `.csproj` package references to point to the
  versions that were just built (or reused).
- It exposes two extensibility events (`OnRoadmapBuild`, `OnFixBuild`) that let other plugins react once a build
  succeeds - in practice this is how `CKli.Publish.Plugin` gets triggered: `CKli.Build.Plugin` computes and builds, and on
  success fires the event that the publish plugin listens to. Building and publishing are deliberately two different
  plugins so that "build" stays a repeatable, side-effect-contained computation while "publish" owns the sensitive,
  one-shot act of pushing artifacts to the world.

### Ecosystem

`BuildPlugin` is constructor-injected with (and therefore depends on) several other StandardPlugins:

| Plugin | Used for |
|---|---|
| [`CKli.VersionTag.Plugin`](../CKli.VersionTag.Plugin) (`VersionTagPlugin`) | Reads/writes version tags (`VersionTagInfo`, `CommitBuildInfo`, `TagCommit`), the source of truth for "what version is this commit/build". |
| [`CKli.BranchModel.Plugin`](../CKli.BranchModel.Plugin) (`BranchModelPlugin`) | Resolves branch names into the World's branch namespace (regular branch vs. its `dev/` counterpart). |
| [`CKli.HotZone.Plugin`](../CKli.HotZone.Plugin) (`HotZonePlugin`) | Computes the `HotGraph` (the dependency graph of solutions for a branch) and the `FixWorkflow` for the `fix build`/`fix publish` commands. |
| `RepositoryBuilderPlugin` (this assembly) | Factory/cache of per-Repo `RepoBuilder` instances that actually run `dotnet build/test/pack`. |
| [`CKli.ArtifactHandler.Plugin`](../CKli.ArtifactHandler.Plugin) (`ArtifactHandlerPlugin`) | Local NuGet feed and `Deployment/` asset handling (`RepoArtifactInfo`), and whether a version's artifacts are already fully available locally. |
| [`CKli.ShallowSolution.Plugin`](../CKli.ShallowSolution.Plugin) (`ShallowSolutionPlugin`) | `MutableSolution`/`PackageMapper`: rewrites `.csproj` package references and detects package update needs. |

`CKli.Publish.Plugin` is not a dependency of `CKli.Build.Plugin` - the relationship is inverted: it depends on
`BuildPlugin` and subscribes to its events, so `CKli.Build.Plugin` has no knowledge of, and no reference to, the publish
plugin.


## How

### `BuildPlugin`

```csharp
public sealed partial class BuildPlugin : PrimaryPluginBase
{
    public BuildPlugin( PrimaryPluginContext primaryContext,
                        VersionTagPlugin versionTags,
                        BranchModelPlugin branchModel,
                        HotZonePlugin hotZone,
                        RepositoryBuilderPlugin repoBuilder,
                        ArtifactHandlerPlugin artifactHandler,
                        ShallowSolutionPlugin solutionPlugin )
    ...
}
```

`BuildPlugin` is a `PrimaryPluginBase` (always instantiated) split across several partial-class files by concern:
`BuildPlugin.cs` (the `build`/`publish`/`*build`/`*publish` commands and the core build call), `BuildPlugin.Fix.cs`
(`fix build`/`fix publish`), `BuildPlugin.Rebuild.cs` (`maintenance rebuild ...`), `BuildPlugin.Issues.cs` (the
`World.Events.Issue` handler), and `BuildPlugin.RoadmapExecutor.cs` (the parallel build engine, a private nested class).
Its behavior is driven by command parameters and by the plugins it depends on, plus the one configuration attribute
below.

#### Configuration

The `<Build>` element of the World definition file carries one optional attribute. It is read - and its
`OnPluginSetAsync`/`PluginInfo` handling implemented - by `RepositoryBuilderPlugin`, which shares that element since
both types live in `CKli.Build.Plugin`:

```xml
<Build DeleteBeforeBuild="$StObjGen" />
```

| Attribute | Default | What it does |
|---|---|---|
| `DeleteBeforeBuild` | *(empty)* | `;` separated list of git ignored files and folders that are deleted from a repository's working folder before it is built. See [`DeleteBeforeBuild`](#deletebeforebuild-not-reusing-what-a-previous-build-generated). |

This is the only Standard Plugin attribute that is **not** a boolean. Like the others it is readable with
[`ckli plugin info`](../../README.md#plugin-info) - which echoes the configured entries - and writable with
[`ckli plugin set DeleteBeforeBuild "$StObjGen"`](../../README.md#plugin-set-name-value) or
`ckli plugin unset DeleteBeforeBuild`, without editing the World definition file by hand. Entries are validated
before being written, so an entry that escapes the working folder cannot be persisted into a configuration that
would then fail every build.

It subscribes to one lifecycle event:

```csharp
World.Events.Issue += IssueRequested;
```

### Commands

All build-family commands share a common set of options (declared once as `const` strings and reused via
`[OptionName]`):

| Option | Meaning |
|---|---|
| `--branch,-b <name>` | Branch to consider. Defaults to the current HEAD (a `dev/` prefix is stripped); if multiple pivot Repos are selected and their checked-out branches differ, it must be specified explicitly. |
| `--max-dop <n>` (positional `maxDop`) | Maximal degree of parallelism for the build. Defaults to 4. |
| `--ci` | Build CI (`dev/` branch) versions instead of regular exploratory/prerelease/stable versions. |
| `--ci.0` | Extends `--ci`: forces a CI version even when a *published* regular version is already available on the commit. It is not needed to switch a pending `local/` release to CI - plain `--ci` rolls that one (see `RollingLocal` below). |
| `skipTests` | Don't run tests even if they never ran locally on the commit (ignored - with a warning - for non-CI builds). |
| `forceTests` | Run tests even if they already ran successfully on the commit. Mutually exclusive with `skipTests`. |
| `--dry-run,-d` | Only compute and display the roadmap; no build/publish is performed (`OnRoadmapBuild` is still raised, with `Roadmap.DryRun == true`). |
| `all` | Consider all Repos of the World as pivots, not only the ones reachable from the current directory. |

| `[CommandPath]` | Method | Description |
|---|---|---|
| `build` | `BuildAsync` | Build-Test-Package and propagate packages from the current repositories to their consumers, keeping everything local (no publish). |
| `publish` | `PublishAsync` | Same as `build`, and publishes the produced artifacts on success. |
| `*build` | `StarBuildAsync` | "Upstream closure" build: also considers the *producers* of the current repositories (not just their consumers), propagating packages downstream, kept local. |
| `*publish` | `StarPublishAsync` | Same as `*build`, and publishes on success. |
| `fix build` | `FixBuildAsync` | Builds the current `FixWorkflow` into the local feed (see below). There is no `--ci`: a fix has no CI line. |
| `fix publish` | `FixPublishAsync` | Builds and publishes the current `FixWorkflow`; on success the workflow is finished. |
| `maintenance rebuild old` | `RebuildOldAsync` | Walks each Repo's stable tags from the newest down, force-rebuilding until one succeeds; tags failing commits `+invalid` (unless `warnOnly`). |
| `maintenance rebuild version` | `RebuildVersionAsync` | Force-rebuilds one specific version tag of the current repository (used to refresh a tag's recorded build content, e.g. to fix lightweight/unreadable tags). |
| `deps update` | `DepsUpdateAsync` | Aligns the World's **external** package dependencies on the versions its World References publish - and, only with `--with-nuget`, on what its feeds offer for the identifiers no reference anchors (see below). |

`build`/`publish` and `*build`/`*publish` differ only in the `isPullBuild` flag passed down to roadmap computation
(`*` commands include upstream producers as pivots); `publish`/`*publish` additionally set `mustPublish: true`. All four
funnel into the same `DoCIAsync`/`DoNonCIAsync` → `ComputeAndDisplayRoadmap` → `DoRunAsync` pipeline described below.

### Events

| Event | Raised by | Payload | Purpose |
|---|---|---|---|
| `BuildPlugin.OnRoadmapBuild` | After a `Roadmap` build succeeds, or immediately for `--dry-run` | `RoadmapBuildEventArgs` (`Roadmap`, `ShouldPublish`, `BuildDate`) | Lets a listener (typically `CKli.Publish.Plugin`) act on the outcome; `Roadmap.SolutionBuildCount` can be 0 (nothing needed building). Handlers call `e.SetFailed()` to fail the command. |
| `BuildPlugin.OnFixBuild` | After all targets of a `FixWorkflow` build succeed | `FixBuildEventArgs` (`FixWorkflow`, `Results`, `KeepBranchOnSuccessfulPublish`, `ShouldPublish`) | Same purpose for the `fix build`/`fix publish` pipeline. |
| `RepositoryBuilderPlugin.OnCoreBuild` | Right before `RepoBuilder` runs `dotnet build/test/pack` for one repository | `CoreBuildEventArgs` (`CommitBuildInfo`, `OutputPath`, `RunTest`, `Cancellation`, settable `ResultHook`) | Lets a handler short-circuit the actual build by setting `ResultHook` to a `BuildResult` - the mechanism used to fake builds in tests. |

Both `RoadmapBuildEventArgs` and `FixBuildEventArgs` derive from `BuildBaseEventArgs`, which centralizes `ShouldPublish`,
a single `BuildDate` for the whole operation, and the `Success`/`SetFailed()` pair.

There is also a static test seam independent of events: `BuildPlugin.SetBuilderFunction(BuilderFunction?)` replaces the
delegate (`BuilderFunction`, default `RealBuildAsync`) that `CoreBuildAsync` calls to perform the actual build - mainly
used to inject a fake builder in tests, and it returns the previous function so callers can chain to the real one.

### `deps update`: aligning the external dependencies

`build` propagates the packages the World *produces*. `deps update` (`BuildPlugin.DepsUpdate.cs` and
`UpgradeMap*.cs`) does the other half: it aligns what the World *consumes* from the outside.

For each package identifier the graph consumes externally
(`HotGraph.Solution.ExternalDependencies`), `UpgradeMap` resolves one target version from two sources, in
this order - and memoizes it, because the identifier set grows while reaching a reference or a feed is the
expensive part:

| Source | Rule |
|---|---|
| The World References' published profiles | A profile's `ProducedPackages` are the first candidates - they are why the reference exists - and win inside a profile, then its `DirectDependencies`, then its regular `TransitiveDependencies`. Two references disagreeing **blocks that package**, not the command. An `AmbiguousDependency` nobody anchors is warned about: with `--with-nuget` the feed will answer instead (and may disagree with that very reference), without it the identifier simply has no target. |
| The World's configured NuGet feeds, **only with `--with-nuget`** | The greatest version they offer. |

**The `<VersionTag><Packages>` configuration is not a source, it is a constraint.** It declares a
[`SVersionBound`](../CKli.VersionTag.Plugin/README.md#configuration) per package identifier - by name or through a
name holding `*` wildcards that covers a whole family - the range of versions this World accepts for it, and that
bound is applied on both ends of the resolution:

- It **caps what a source may propose**: a reference or a feed version outside the bound is refused
  (`TargetState.OutOfBound`, reported apart as "held back by the World `<Packages>` configuration" - a deliberate
  hold is worth seeing, not worth hiding). A feed lookup filters its candidates on the bound first, so
  `3.2.1[LockMajor]` tracks the greatest `3.x` and ignores a published `4.0.0` instead of blocking on it.
- It is an **invariant to restore**: a repository that references the identifier *outside* its bound is brought
  back to the bound's base version, even when no source offers anything at all (`TargetState.Bound`). This is
  what makes a bound a way to *drive* an update and not only to block one.

A `[Lock]`ed bound is the old pin: the only accepted version is the base one, so nothing can ever move the
identifier above it - and every repository below it is brought up to it.

The bound that applies to an identifier is the one of **the first `<Package>` that matches it** - the declaration
order is the priority, like the routes of a web router - and it is reported **with the `Name` that carries it**
("is not in the configured bound 8.0.0[LockMajor] of `<Package Name="Microsoft.AspNetCore.*" />`"): the reach of a
pattern is exactly what the package identifier alone doesn't show, and `--dry-run` is where it is meant to be
checked.

**The World References are the default source and the feeds are opt-in.** Without `--with-nuget` no feed is
queried at all - not even read from the `<ArtifactHandler>` configuration, since `GetConfiguredNuGetFeeds`
*writes* the default nuget.org feed into a World that configures none, and an analysis that will never query a
feed must not do that. An identifier no reference carries is then left alone rather than aligned on whatever a
feed happens to publish today: what a World consumes is decided by the Worlds it depends on, and pulling in the
outside world's latest is a deliberate, separate act. A World that has no `<Reference>` at all and doesn't pass
the flag is warned that nothing can anchor a target - it is a dead end, not a detail. `--prerelease` and
`--stable` only filter what a feed offers, so they are **refused** without `--with-nuget` rather than silently
ignored.

When the feeds do answer, there is no finer version filter than stable/not: `CSVersionKindFilter` rejects every
non-CSemVer version, so most third party prereleases (`2.0.0-rc.2.23479.6`) would never be seen. Plain SemVer
precedence applies - a root branch takes only `SVersion.IsStable`, a prerelease or exploratory branch takes
everything - and a CI version is never a target (the CI notion here lives in the References, not in a feed).

The report renders each participating repository with the very same row as a build roadmap - the pivot marker
and the linked, dirty-aware name - see [The repository row](#the-repository-row-one-rendering-two-commands).

**Who participates.** The pivots to start with, exactly as the build commands compute them. Then an upstream
that needs an upgrade joins: it will be rebuilt, and a repository we open a branch on and rebuild is a
first-class participant. Unless `--narrow`, the **downstreams** of an updated participant join too: `build`
already rebuilds every transitive downstream of anything it builds, so that wide set is exactly what the
build that follows would touch - the report shows the real blast radius rather than half of it.

**The World is required to be coherent, never made coherent.** `HotZonePlugin.CheckBasicPreconditions` first
(no dirty repository - `GitRepository.Commit` stages everything, so a commit would sweep uncommitted work in -
no version tag issue, no branch model issue), then a fetch, then a **refusal** when a branch is behind its
tracked remote ("run `ckli pull` first"). The fetch is what makes that check mean anything: the command is
online by design (it reads the References over http, and with `--with-nuget` queries every feed), so being
stale about our own repositories would be incoherent.

That fetch is `ckli fetch` applied to the whole World and it is parallelized the same way: an
`ActivityMonitorAsyncPool` bounded by `--max-dop` (unbounded by default), with
`ParallelErrorBehavior.SoftStop` - a repository is independent of the others here (a fetch moves remote
tracking references only, no branch and no file), and the first failure aborts the command anyway. Note that
this is *not* the whole online cost of the command: reading the World References' published profiles and
querying the feeds are still serial.

**What applying does**, per participant, upstreams first and all of it repo-local: `EnsureExists` - which
creates a missing branch at `HotBranch.GetStartCommit`, *the very commit whose content was analyzed* - then
`EnsureDevBranch`, a checkout of the `dev/` branch, `MutableSolution.UpdatePackages` with an **exact**
`PackageMapper` (only the version that repository really references is rewritten) and a commit naming what
moved. `Synchronize` is deliberately **not** called: a stale World has already been refused, so it has nothing
to do, and it is the only step that could move a tip away from what the report describes. That is what keeps
`--dry-run` and the write on the same content.

| Option | Meaning |
|---|---|
| `--branch,-b <name>` | As for the build commands. |
| `all` | Consider all the Repos as pivots. |
| `narrow` | Keep the update to the pivots and their upstreams: don't bring the downstreams of an updated repository in. |
| `noFetch` | Don't fetch first. The analysis is then only as fresh as the last fetch - and the divergence refusal cannot fire. |
| `--max-dop <n>` (positional `maxDop`) | Limits the parallelism of the fetch. Unbounded by default, exactly as for `ckli fetch`. Refused with `--no-fetch`: it would silently do nothing. |
| `ci` | Consider the **CI published profiles** of the World References. Note that this is not the `--ci` of the build commands: nothing is built here, and the graph is always computed with `isCIBuild: false`. A published folder holds at most one alive CI profile per branch and it is newer than every non-CI publication of that branch, so the one that is there simply applies - there is no "is it superseded" question to answer. |
| `with-nuget` | Let the World's NuGet feeds answer the identifiers no World Reference anchors. Without it no feed is queried and the References are the only source. |
| `prerelease` / `stable` | Override the stable/not filter of the feeds. Mutually exclusive, and both require `--with-nuget`. |
| `allowDowngrade` | Apply the updates that move a version **down**. A World Reference may legitimately pin lower than what this World references - alignment is the point - but without this flag a map containing a downgrade reports and writes nothing. |
| `--by-repo` | Group the report by repository instead of by package. Display only - it changes nothing about what is computed or written. |
| `--dry-run,-d` | Only display the upgrades. |

#### The report: by package, or `--by-repo`

The report answers "where is this package used, and what moves": one row per package with the version it moves
**to** and where that target comes from, then one row per version it moves **from** with the repositories that
are on it, greatest version first so the most behind come last.

```
Dependency upgrades of branch 'stable':
CK.CanaryPackage → 1.0.0  (NuGet)
    ▲ 0.9.0  X-Middle, X-Sample
    ▲ 0.8.0  X-Core
3 upgrade(s) of 1 package(s) in 3 repositories.
```

Four things this view does deliberately:

- **The arrow sits on the version group, not on the package.** A package has one target version but several
  source versions, and one of them can be above the target while another is below it - a single arrow on the
  package row would be wrong for one of them.
- **The origin is stated once.** `(NuGet)`, the anchoring reference or the configured bound is named once per
  package rather than on every row, so the full text fits.
- **The version column is as wide as the widest version of the whole report**, not of its own package, so the
  repository lists form one straight column down the report. A `TextBlock` trims its content, so that padding
  is a right margin and never trailing spaces.
- **No pivot marker, and the names are inline.** The grouping is about packages, not about where a repository
  sits in the graph. Each name keeps its link to the working folder (`Repo.ToLinkedNameRenderable`, the inner
  half of the shared [repository row](#the-repository-row-one-rendering-two-commands)), and the branch creation
  note - a property of a repository, not of a package - is said once at the end instead of under every package
  that repository appears in.
- **The list of names is a `FlowContent`, not a row of cells.** A package that 60 repositories reference breaks
  into as many lines as the screen needs, each continuation line indented under the version column. It cannot be
  a `HorizontalContent`: those cells are columns sharing the width, so such a row does not fit any screen and
  every name ends up wrapped inside 10 columns (see `CKli.Core`'s README). Each name carries the comma that
  follows it as one cell, so a line never opens with a separator.

That a package has exactly **one** target version is what makes this grouping lossless: `Target.GetUpgrade`
answers either the resolved `Version` or the configured bound's `Base`, and both are package scoped.

`--by-repo` gives the other orientation - one row per repository, the build roadmap's row with its pivot marker,
then the upgrades it receives below it. It is the view to reach for when the question is "what happens to this
repository" rather than "where does this package move".


### The `Roadmap`: computing what to build

`Roadmap` (in `Roadmap.cs` + partials `Roadmap.BuildSolution.cs`, `Roadmap.BuildInfo.cs`, `Roadmap.BuildSolutionList.cs`,
`Roadmap.Mapping.cs`, `Roadmap.MustBuildReason.cs`) augments the `HotGraph` (and its `Solution`s) coming from
`CKli.HotZone.Plugin` with build decisions:

```csharp
var hotGraph = _hotZone.GetHotGraph( monitor, branchName, ciBuildMode != CIBuildMode.None, pivots );
var roadmap  = Roadmap.Create( monitor, _versionTag, _artifactHandler, hotGraph, isPullBuild, ciBuildMode, mustPublish, dryRun );
```

For every `HotGraph.Solution` (ordered topologically, `OrderedSolutions`), a `Roadmap.BuildSolution` is created and its
`Initialize` method decides `MustBuildReason` - a `[Flags]` enum:

| Reason | Meaning |
|---|---|
| `UpstreamBuild` | A direct upstream (producer) solution must itself be built. |
| `UpstreamVersion` | The last build consumed a package at a version that upstream builds have since changed. |
| `FakeVersion` / `DeprecatedVersion` | The last built version tag is a `+fake` or `+deprecated` marker - never "skippable". |
| `DependencyUpdate` | A package reference must move to a version coming from `<VersionTag>` plugin configuration or from cross-repo discrepancy resolution ("C"/"D" updates - "U" updates from already-built upstream packages are, by themselves, skippable). |
| `CodeChange` | The commit's own code changed since the last build (conventional-commit/version-tag driven). |
| `CI0` | `--ci.0` is used, there is no other reason to build, and the last version is a *published* non-CI build with no `ci.0` yet on this commit - forces a `ci.0` rebuild, opening a new version line above the published one. |
| `RollingLocal` | A CI build is asked for (plain `--ci` is enough), there is no other reason to build, and the last version is a non-CI build still pending as a `local/`/`building/` release - the CI version takes its place on the same commit. |

`CI0` and `RollingLocal` are the two halves of "the commit already carries a version, build it in CI anyway", split
on whether that version is published. Both are guarded by `buildReason == None`, so each can only ever be the *sole*
reason to build.

**Why `RollingLocal` needs no flag.** A pending `local/` release is unpublished by construction: nothing consumed it,
so there is no version line to protect. `TagCommit.CanBearVersion` already sanctions exactly this - its "rolling local
build" case (`IsBuildingOrLocal && Version.BranchName == version.BranchName`) lets the CI version take the commit, and
`ApplyReleaseBuildTag` destroys the old one through `DestroyLocalReleases`. Before `RollingLocal` existed that
permission was simply never exercised from `--ci`: no reason to build was ever produced, `--ci` answered *"There is
nothing to build"* and returned true, and only `--ci.0` got there. A developer who ran a regular `ckli build` by
mistake had no way to learn that. Destroying the pending release is a side effect the user did not name, so the
roadmap `monitor.Warn`s it - only when `RollingLocal` is the sole reason, since superseding a `local/` release while
building for any other reason is the ordinary rolling local build and needs no warning.

A *published* version is the opposite case: it cannot be reclaimed, `--ci.0` opens `vX.Y.(Z+1)--ci.0` above it and
leaves the published tag alone. When a plain `--ci` roadmap comes out empty and some commits are in that situation,
the summary names the option: *"(Use '--ci.0' to build a CI version from the N repositories that already carry a
released version.)"* (`BuildSolution.IsCIForceCandidate` feeds `RStats.ciForceCandidateCount`).

When `MustBuildReason.None`, the build is skipped and the `BuildSolution.BuildInfo.TargetVersion` is simply the last
built version (so downstream solutions still see a consistent version). Otherwise `BuildInfo.TargetVersion` is computed
via `TagCommitTree.ComputeTargetVersion` and stamped with a `"building/"` prefix pending a successful build. Either way
it is the version the solution will produce once the roadmap is done, exposed as `BuildSolution.TargetVersion`; since
`SVersion` equality ignores the `"building/"` prefix, it compares directly with the versions recorded by consumers.

Every solution of an initialized roadmap has a `BuildInfo`: `Initialize` sets one on each of them (the field is only
null while that initialization runs).

A solution can also be entirely **out of scope**: with `*build`/`*publish` pivots include upstream producers
(`isPullBuild: true`); with plain `build`/`publish`, a non-pivot solution whose only reason to build would be a
skippable one (`CodeChange`/`DependencyUpdate`("U")/`CI0`/`RollingLocal`) is left un-built - this is the only place the "star" vs
non-star distinction actually changes the outcome (`canSkip` in `BuildSolution.Initialize`). Skipping therefore only
ever happens in the non-star, has-pivots case: `*build`/`*publish` and a stack-root/`--all` roadmap (where no solution
is a pivot) never skip anything.

A skipped solution can be left holding **pending "U" updates**: its sources reference packages produced by this World
in versions that have been superseded. This is not an error - since the solution is not built, nothing it produces
enters the build - so it is warned about, kept on its `BuildInfo` (`UUpdates`, surfaced as
`BuildSolution.HasPendingUpdates`) and rendered on its row. The misalignment survives the build and is only resolved by
building that solution, at which point the same "U" update becomes a `MustBuildReason.DependencyUpdate`. "C" and "D"
updates always trigger a build, so only "U" ones can be left pending.

`Roadmap.Create` loops `Initialize` + `HotGraph.ConsiderBuildImpact` until stable (`hasChanged == false`): considering
that a solution must build can itself change the graph (e.g. surface additional `dev/` solutions), so the roadmap is
recomputed until no new build requirement appears.

`Roadmap.PackageMapping` (`Roadmap.Mapping`, `IPackageMapping`) is what the actual build uses to rewrite `.csproj`
references: for packages produced inside the World it resolves to the roadmap's own target/last-built versions; for
everything else it falls back to `HotGraph.PackageUpdater`'s World-configured and discrepancy mappings.

`Roadmap.PublishableStatus` / `BuildSolution.PublishableStatus` (`None` < `AlreadyPublished` < `PublishRequired` <
`Build` < `IndirectPublishRequired` < `BuildingPending`, in that numeric/severity order) summarize, across all
solutions, what publishing the roadmap would mean or why it can't be done yet (e.g. `BuildingPending` when an upstream's
last build failed). `None` is the value before `ConcludeInitialization` ran: every solution of an initialized roadmap
has a greater one.

Whether the roadmap may actually be published is decided beyond this status, by
[`CKli.Publish.Plugin`](../CKli.Publish.Plugin)'s publication gate, which checks that the profile of packages the
publication would leave on the branch is coherent.


### The repository row: one rendering, two commands

`build` and `deps update` render a repository the same way, because they render it with the same code rather
than with two implementations that agree today:

| Piece | Where it lives | What it says |
|---|---|---|
| The pivot marker | `HotGraph.Solution.ToPivotPrefixRenderable` (`CKli.HotZone.Plugin`) | How the repository relates to the [pivots](#the-roadmap-computing-what-to-build). |
| The name | `Repo.ToNameRenderable` (`CKli.Core`) | The `DisplayPath`, linked to the working folder, preceded by `✱` when the repository is dirty. |

The marker has nine cases, all exactly **3 columns wide** - so a column of them aligns with no table layout
involved - and it is displayed only when `HotGraph.HasPivots` is true. When it is false every repository is
(or is not) a pivot, so the three flags are all false and the whole column is dropped rather than marking
every row: that is why `--all` (where every repository is a pivot) shows no marker at all.

| Marker | Meaning |
|---|---|
| `⊙` | A pivot, with nothing of the graph on either side of it. |
| `→⊙` | A pivot that has pivots upstream (it produces for them). |
| `⊙→` | A pivot that has pivots downstream (they consume from it). |
| `→⊙→` | A pivot with pivots on both sides. |
| `·` / `→·` / `·→` / `→·→` | The same four, for a repository that is **not** a pivot but participates. |
| *(blank)* | Neither a pivot nor related to one. |

The name's colour is the shared `Repo.ToNameRenderable( screen, willBeWritten )` convention, not a per command
choice: **green means the command is going to write to this repository** - built, for a roadmap; updated, for
`deps update`, which only ever lists repositories it will write to. Gray is a repository that is only shown for
context, and a dirty repository takes the red of the same pair. (`deps update` refuses a World with any dirty
repository up front, so its `✱` branch is correct but unreachable.)

### Building the roadmap: `RoadmapExecutor`

`BuildPlugin.RoadmapExecutor` (nested in `BuildPlugin.RoadmapExecutor.cs`) drives the actual builds once a roadmap has
been computed and is not a dry-run:

- **Single solution** (`Roadmap.SolutionBuildCount == 1`): built directly, no channel/monitor-pool machinery.
- **Multiple solutions**: each `BuildSolution` that must build has a `Roadmap.BuildInfo.BuildAsync(executor)` task that
  first awaits its own `DirectRequirements`' build tasks (recursively, memoized via a `Lock`-guarded `_buildTask`) and
  only then calls `RoadmapExecutor.ParallelBuildAsync`, so the dependency order is enforced by the task graph itself
  rather than by an explicit scheduler loop.
- Parallelism is bounded by `--max-dop` through a hand-written **`IActivityMonitor` pool**: an unbounded `Channel<object>`
  carries `MonitorRequest`s (asking to acquire a monitor) and results; a single `RunLoopAsync` consumer hands out
  monitors up to `maxDop`, queueing extra requests (`waitingQueue`) until one is released. Each acquired monitor is a
  fresh `ActivityMonitor` up to `maxDop` instances, reused across builds. This exists so N repositories can build
  concurrently while each gets its own log scope, without spawning unbounded loggers.
- Per-solution build (`DoBuildAsync`) does, for the target repo: ensure/checkout the right branch (`dev/` for CI builds,
  or integrate `dev/` into the regular branch first for non-CI builds), handle a possible version-tag clash on the same
  commit (creates an empty commit when needed so the new version has its own commit), rewrite package references via
  `MutableSolution`/`PackageMapper` and commit ("Updated dependencies." with `AmendIfPossibleAndPrependPreviousMessage`
  when a merge commit already carries the change), then delegate to `BuildPlugin.CoreBuildAsync`.
- On overall success, every built `Roadmap.BuildInfo.CommitBuilding()` promotes its `"building/"` version tag to
  `"local/"` (or, for un-built solutions, promotes an already-`"building/"` last-build tag the same way) - see
  `BuildResult.CommitBuilding()`.
- Before returning, `Roadmap.BuildAsync` refuses to start at all if any involved Repo's working folder is dirty.

### Core build: `CoreBuildAsync` → `RepoBuilder`

`BuildPlugin.CoreBuildAsync` is the single entry point used by the roadmap executor, the `fix` pipeline, the
`maintenance rebuild` commands, and the version-tag issue fixers:

1. Resolves the per-Repo `RepoBuilder` from `RepositoryBuilderPlugin`.
2. Decides whether tests must run (`RepoBuilder.HasTestRun`, a persisted cache keyed by commit tree SHA, unless
   `runTest` was forced by the caller).
3. Unless `forceRebuild`, checks whether the target version's tag already exists with all its artifacts locally
   available (`ArtifactHandlerPlugin.HasAllArtifacts`) - if so and tests don't need to (re)run, the build is skipped and
   a `BuildResult` with `SkippedBuild: true` is returned immediately ("Useless build ... skipped").
4. Obtains a `CommitBuildInfo` (`VersionTagInfo.TryGetCommitBuildInfo`).
5. Calls `RepositoryBuilderPlugin.DeleteBeforeBuild` (see below), then the injected `BuilderFunction`
   (`RealBuildAsync` by default).

#### `DeleteBeforeBuild`: not reusing what a previous build generated

A repository can hold generated code that is compiled with it - `$StObjGen/G0.cs` at the root of every project using
the CKomposable code generation, for instance - produced either by a previous run of its own tests or by the build of
another project of the same solution. It is git ignored, so a fresh CI clone never has it, but a developer machine
does, and nothing else removes it: the hard reset done after a build uses `deleteIgnored: false` on purpose (see
`GitRepository.ResetHard`, whose doc explains the cost of enumerating ignored entries such as a `node_modules`).

`<Build DeleteBeforeBuild="$StObjGen" />` names what must go. Entries follow the `.gitignore` rule - no `/` matches at
any depth, a `/` anchors the entry at the working folder, and the name part accepts the `*` and `?` wildcards - and a
single entry covers both files and folders. Folders are deleted rather than emptied: that is exactly what a fresh clone
looks like, and the post-build reset removes empty folders anyway.

**Only git ignored content can be deleted.** A build must produce the artifacts of the commit that it tags, so deleting
tracked content would build something else - and the hard reset afterwards would restore the file and hide it. An entry
matching non ignored content fails the build with an error naming it.

This runs in `CoreBuildAsync` rather than in `RepoBuilder.BuildAsync` because it is a property of the build itself, not
of the `BuilderFunction` that happens to be installed: `CKli.Build.Plugin.Testing` replaces that function wholesale, so
a hook inside `RepoBuilder` would never run under the fake harness (nor under a specialized `RepoBuilder`).

`RealBuildAsync` checks out the build commit (detached HEAD) if the working tree isn't already on it, calls
`RepoBuilder.BuildAsync`, and restores the original branch afterward - regardless of success.

`RepoBuilder` (`RepoBuilder/RepoBuilder.cs`, a `RepoInfo` created and cached per-Repo by `RepositoryBuilderPlugin : PrimaryRepoPlugin<RepoBuilder>`) does the concrete work:

1. Adds the World's local NuGet feed (and any artifact-handler-configured feeds) to `nuget.config`, working around a
   `PackageSourceMapping`/local-feed NuGet issue.
2. Raises `RepositoryBuilderPlugin.OnCoreBuild` - if a handler sets `CoreBuildEventArgs.ResultHook`, the real
   build/test/pack steps are skipped entirely and that result is used (the test-injection point).
3. Otherwise calls `DotNetBuildTestPack` (`protected virtual`, overridable by a specialized `RepoBuilder`): `dotnet
   build` (with `/p:Version`, `/p:InformationalVersion`, `/p:FileVersion`, `ContinuousIntegrationBuild=true`), optionally
   `dotnet test`, then `dotnet pack` to a temporary output folder.
4. On success: hard-resets the working folder, runs the `Deployment/GenerateAssets.cs` convention if present (via
   `dotnet run` and `ArtifactHandlerPlugin`'s asset publishing), publishes the packed packages to the local NuGet feed
   (`RepoArtifactInfo.PublishToNuGetLocalFeed`), and applies the release build tag
   (`CommitBuildInfo.ApplyReleaseBuildTag`) to produce the final `BuildResult`.
5. The working folder is always reset (hard reset + empty-folder cleanup) in a `finally`, whether or not the build
   succeeded.

`RepoBuilder.HasTestRun`/the successful-test cache is backed by `LocalStringCache` (`RepoBuilder/LocalStringCache.cs`): a
trivial newline-delimited set of keys persisted under the World's `$Local` folder (`TestRun.Sha.txt`), shared by every
Repo of the World, with no eviction/housekeeping - it exists purely to let `skipTests`-by-default behavior survive
across CKli invocations on the same machine.

### `fix build` / `fix publish`: the Fix Workflow

`BuildPlugin.Fix.cs` builds a `FixWorkflow` (owned by `CKli.HotZone.Plugin`, loaded via `FixWorkflow.Load(monitor, World,
out workflow)`) - a sequence of specific past versions, in specific repositories, that must be rebuilt with corrected
code without changing their public package surface ("fixing" a release rather than releasing a new one). Unlike the
roadmap executor, targets are built **sequentially**, in `workflow.Targets` order, stopping at the first failure:

- Each target's branch is checked out at the depth of the commit being fixed (`CheckoutFixTargetBranch`), package
  references are updated using a **`FixPackageMapper`** (`IPackageMapping`) instead of the roadmap's `Mapping` - it
  tolerates being asked for a version one patch below what it has a mapping for, since a fix workflow patches an older
  release rather than the current head.
- Extensive clash handling deals with a commit that already carries a CI or local version tag (from a previous `fix
  build --ci`, for instance): destroying a still-local CI build so it can be superseded, or creating an empty commit to
  carry a new version over an already-published one - see the in-code comments in `BuildOneFixTargetAsync` for the full
  "aggressive / gentle / gentle synthesis" reasoning.
- After a successful build, the produced package identifiers are compared against the version being fixed
  (`toFix.BuildContentInfo.Produced`) and the fix is **rejected** if they differ - a fix must not change what a
  repository produces.
- On success for every target, `"building/"` tags are committed to `"local/"` and `OnFixBuild` is raised; `fix publish`
  additionally requests publication and, unless `--keep-branch` (or a CI build) was requested, lets the publish plugin
  delete the now-integrated `fix/` branches.

### Issues: version-tag housekeeping

`BuildPlugin.Issues.cs` subscribes to `World.Events.Issue` and inspects each Repo's `VersionTagInfo` for two situations:

- **`TagsRebuildIssue`** - lightweight tags among the regular version tags (should be annotated) or annotated tags whose
  message can't be parsed. `ExecuteAsync` (i.e. `ckli issue --fix`) rebuilds each one via `CoreBuildAsync` with
  `forceRebuild: true` to recompute and re-store its build content info, replacing the tag if its canonical name changed.
- **`NoVersionTagIssue`** - a repository with no version tag at all but a resolvable branch-model root: `ExecuteAsync`
  creates an initial `v0.0.0+fake` tag (or based on `VersionTagInfo.InfVersion`) on the root branch's tip so the
  repository enters the normal versioning flow.

### Supporting types

| Type | Role |
|---|---|
| `CIBuildMode` (internal enum: `None`, `CI`, `CIForce`) | Distinguishes plain builds from `--ci` and `--ci.0` builds; threaded through `Roadmap`/`BuildSolution` decisions. |
| `PublishableStatus` | See above - ordered enum combined by taking the maximum across solutions. |
| `BuilderFunction` (delegate) | The pluggable "actually build this repo" seam used by `CoreBuildAsync`/`SetBuilderFunction`. |
| `FixPackageMapper` | Patch-tolerant `IPackageMapping` used only by the fix workflow. |
| `LocalStringCache` | Generic `$Local`-backed string-set cache; currently used only for the test-run SHA cache. |

### A note on `RebuildOldAsync`/`RebuildVersionAsync`

These two `maintenance rebuild` commands are maintenance tools rather than part of the normal build flow: they replay a
past, already-tagged commit through `CoreBuildAsync` with `forceRebuild: true` to verify it still builds (`rebuild old`
walks stable tags from newest to oldest until one succeeds, tagging failing commits `+invalid` unless `--warn-only`;
`rebuild version` targets one specific version of the current repository). They are typically used to validate the
historical release chain still compiles/tests cleanly, or to force-refresh a tag's recorded build content.
