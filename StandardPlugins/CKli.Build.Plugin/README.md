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
It has **no XML configuration** of its own in the World definition file; all its behavior is driven by command
parameters and by the plugins it depends on.

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
| `--ci.0` | Extends `--ci`: forces a `ci.0` version even when a regular version is already available on the commit. |
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
| `CI0` | `--ci.0` is used, there is no other reason to build, and the last version is not yet a CI build on this commit - forces a `ci.0` rebuild. |

When `MustBuildReason.None`, the build is skipped and the `BuildSolution.BuildInfo.TargetVersion` is simply the last
built version (so downstream solutions still see a consistent version). Otherwise `BuildInfo.TargetVersion` is computed
via `TagCommitTree.ComputeTargetVersion` and stamped with a `"building/"` prefix pending a successful build. Either way
it is the version the solution will offer once the roadmap is done, exposed as `BuildSolution.TargetVersion`; since
`SVersion` equality ignores the `"building/"` prefix, it compares directly with the versions recorded by consumers.

Every solution of an initialized roadmap has a `BuildInfo`: `Initialize` sets one on each of them (the field is only
null while that initialization runs).

A solution can also be entirely **out of scope**: with `*build`/`*publish` pivots include upstream producers
(`isPullBuild: true`); with plain `build`/`publish`, a non-pivot solution whose only reason to build would be a
skippable one (`CodeChange`/`DependencyUpdate`("U")/`CI0`) is left un-built - this is the only place the "star" vs
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
4. Obtains a `CommitBuildInfo` (`VersionTagInfo.TryGetCommitBuildInfo`) and calls the injected `BuilderFunction`
   (`RealBuildAsync` by default).

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
