# CKli.Publish.Plugin

`CKli.Publish.Plugin` is the plugin that performs the **remote** side of a release: pushing built
NuGet packages to configured feeds, creating (draft, then finalized) releases on the repository's
Git hosting provider, uploading generated asset files to those releases, and pushing the version
tag and integration branches that make the release visible to everyone else.

## Why

`CKli.Build.Plugin` is deliberately the "conductor" of a build: it resolves what needs building
across a `Roadmap` (or a `FixWorkflow` for patch releases), builds it, packs NuGet packages into
the World's *local* feed (`$Local/<World>/NuGet`, managed by `CKli.ArtifactHandler.Plugin`), and
tags the commit — but it never talks to a remote feed or a Git host. Once it has decided
publication is required, it raises `BuildPlugin.OnRoadmapBuild` / `BuildPlugin.OnFixBuild` with a
`ShouldPublish` flag and expects a listener to do the actual remote work.

`CKli.Publish.Plugin` is that listener. It has no commands of its own — in particular it does
**not** implement `*publish`/`*build fix publish` (those live in `CKli.Build.Plugin`, see its
README) — it is a purely event-driven plugin that turns "what was built locally" into "what is
now public": pushed packages, a pushed version tag, a hosted release, and pushed branches.

It sits downstream of, and depends on:

| Plugin | What `CKli.Publish.Plugin` uses it for |
|---|---|
| `CKli.Build.Plugin` | Source of the `OnRoadmapBuild` / `OnFixBuild` events, `Roadmap`, `BuildResult`, `BuildContentInfo`, `FixWorkflow`. |
| `CKli.ArtifactHandler.Plugin` | `GetConfiguredNuGetFeeds` (World's `<NuGet><Feed>` configuration), `GetAssetsFolder` (where produced asset files live locally), `DestroyLocalRelease` (post-publish cleanup of `$Local`). |
| `CKli.BranchModel.Plugin` | `BranchNamespace` — maps a version's `CSVersionKind` to the `BranchName` (and its `Index`) that owns it, used to pick which configured feeds/senders apply. `BranchName.VersionKind` and `BranchName.ExploratoryName` also place a profile's version on the branch that produced it. |
| `CKli.VersionTag.Plugin` | `EnsureDatabase` / release-info graph used to resolve indirect publication requirements (see caveat below). |
| `CKli.HotZone.Plugin` | Indirectly, through `FixWorkflow` (owned by `HotZone`) which `CKli.Build.Plugin` passes along in `OnFixBuild`. |
| `CK.Packaging.Abstractions` | The [`PublishedProfile`](https://github.com/CK-Build/CK-Packaging-Abstractions/blob/stable/CK.Packaging.Abstractions/README.md) contract: the immutable, serializable description of what a publication offers. It arrives transitively through `CKli.Core`. |

Because the plugin only reacts to events, a World doesn't need to configure it explicitly to
"enable" publishing — declaring it (or one of its NuGet package dependents) in the
`<WorldName>-Plugins` solution and letting `CKli.Core`'s constructor injection wire it to
`BuildPlugin` is enough. It has **no XML configuration section of its own** under `<Plugins>` in
the World file; all the configuration it reads (NuGet feeds, push credentials, quality filters)
belongs to `CKli.ArtifactHandler.Plugin`.

## How

### `PublishPlugin` — the entry point

```csharp
public sealed class PublishPlugin : PrimaryPluginBase
{
    public PublishPlugin( PrimaryPluginContext primaryContext,
                           BuildPlugin build,
                           ArtifactHandlerPlugin artifactHandler,
                           BranchModelPlugin branchModel,
                           VersionTagPlugin versionTag ) : base( primaryContext ) { ... }
}
```

`PublishPlugin` derives from `PrimaryPluginBase` (always instantiated) and takes the four
collaborator plugins above through constructor injection. In its constructor it subscribes to:

- `_build.OnRoadmapBuild.Async += OnRoadmapBuildAsync`
- `_build.OnFixBuild.Async += OnFixBuildAsync`

It defines no `[CommandPath]` methods and does not subscribe to `World.Events` — all of its work
is triggered from these two build events.

#### `OnRoadmapBuildAsync` — regular `publish` / `*publish`

When `e.ShouldPublish` is true, and the roadmap's `PublishableStatus` is strictly between
`AlreadyPublished` and `BuildingPending` (i.e. there is something publishable and nothing is
blocking it):

1. `PublishRoadmap.Create(monitor, roadmap, versionTag)` computes the **publication gate** (see
   below) and renders its verdict to the screen.
2. Unless `roadmap.DryRun` is set: the gate must be open (`publish.CanPublish`), a `PackageSender`
   is created from the World's configured NuGet feeds, a free profile version is minted from the
   `PublishedFolder` (see [The profile version](#the-profile-version)), and
   `PublishRoadmap.PublishAsync` runs the actual publication with a `RoadmapPublisher` and an
   `IndirectPublisher`.
3. On success, the `FinalProfile` is added to the `PublishedFolder` and `Save`d, then
   `World.StackRepository.PushChanges` commits and pushes it along with everything else the Stack
   accumulated. A failure to write the profile file is logged and does **not** fail the publication:
   the publication itself cannot be undone.
4. On failure (closed gate, `packageSender == null`, or `PublishAsync` returning `false`),
   `e.SetFailed()` is called, which fails the owning command.

A `--dry-run` stops after step 1: it only reports. This mirrors how a `BuildingPending` roadmap is
handled — the verdict is displayed, and it is the real publication that fails.

#### `OnFixBuildAsync` — `fix build` / `fix publish`

When `e.ShouldPublish` is true, it builds a `FixPublisher` and publishes each `FixWorkflow` target
with its `BuildResult` (a fix workflow is always a flat list of targets, never a dependency
roadmap). On success, and only for a non-CI build:

- Unless `e.KeepBranchOnSuccessfulPublish` is set, every target's local+remote+tracked
  `fix/vMajor.Minor` branch is deleted (`DeleteGitBranchMode.WithTrackedAndRemoteBranch`) — errors
  here are logged but ignored, since intermediate (halted) fix publications must keep their
  pushed branches for a retry, and only the final successful one cleans up.
- `FixWorkflow.DeleteCurrent(monitor, world)` removes the persisted fix-workflow state file.

On failure, `e.SetFailed()` is called.

### `PublishRoadmap` — the gate and the publish loop

`PublishRoadmap` (in `Roadmap/PublishRoadmap.cs`) wraps a `Roadmap` with its publication gate:

| Member | Meaning |
|---|---|
| `Gate` (`PublishedProfileBuilder`) | Whether the profile this publication would leave on the roadmap's branch is coherent, and what must happen for it to be. |
| `CanPublish` | `Status < PublishableStatus.BuildingPending` and `Gate.IsValid`. |
| `FinalProfile` (`PublishedProfile?`) | The profile this publication offers. Available once `PublishAsync` has run, and on success this is what `PublishPlugin` stores in the `PublishedFolder`. |
| `DirectBuildingAliens` / `DirectAlreadyPublished` | Solutions directly blocking (`BuildingPending`) or already done. |

`PublishAsync` runs three steps, and pushes nothing until all of them are satisfied:

1. `Gate.BuildFinalProfile` builds the profile from the real build and tag content. A conflict here
   aborts the publication.
2. Every `Gate.RequiredPublications` release is published by the `IndirectPublisher`, producers
   first.
3. Every solution whose `PublishableStatus` is `Build` or `PublishRequired` is published by the
   `RoadmapPublisher`, in `OrderedSolutions` order.

### `PublishedProfileBuilder` — the publication gate

A World's **published profile** on a branch is the set of packages it offers there: exactly one
version per package identifier, with every version required by one of them being the one the
profile offers. `PublishedProfileBuilder` (`Roadmap/PublishedProfileBuilder.cs`) decides whether
publishing a roadmap would leave that profile coherent.

The gate works at the **solution** level. Every package a solution produces carries that solution's
single version, and a package identifier is produced by exactly one solution
(`HotGraph.ProducedPackages`), so the version a package is offered in is a function of its producing
solution — `Roadmap.BuildSolution.TargetVersion`. Package identifiers are only the join key: the
version a consumer was built against comes from its recorded `BuildContentInfo.Consumed` and is
mapped back to the solution that produces it.

`Create(monitor, roadmap, versionTag)` runs **before any build**, so a `--dry-run` gets the same
verdict as a real publication:

| Member | Meaning |
|---|---|
| `IsValid` | No `Discrepancies`, no `BuildingAliens`, no `MissingArtifacts`. |
| `Discrepancies` (`ImmutableArray<Discrepancy>`) | `(Consumer, Producer, Required, Offered)`: a solution that is not built recorded a requirement that disagrees with the version its producer will offer. Only solutions that are *not* built can disagree — a built solution has its World references rewritten from the `Roadmap.PackageMapping`, which is that very offer. |
| `SolutionsToBuild` | The `Discrepancy.Consumer` solutions. Building them realigns their references. |
| `RequiredPublications` (`ImmutableArray<RepoReleaseInfo>`) | The `local/` releases (built but unpublished, on another branch) that must be published first, discovered by walking `VersionTagPlugin`'s release-info graph from each `IndirectPublishRequired` solution. Ordered producers first. |
| `BuildingAliens` | `building/` releases found in that closure — a failed or incomplete build, nothing to publish. Blocking. |
| `MissingArtifacts` | `local/` releases whose artifacts are gone from `$Local` (`RepoReleaseInfo.HasAllLocalArtifacts`): only a rebuild can fix them. Blocking. |
| `AlreadyPublishedAliens` | Already published releases found in that closure — a previous publication was incomplete. Warning only. |

The gate is **per branch**, and only the branch being published is gated: a publication on a cooler
branch always invalidates the profiles of the hotter ones, which heal through their own next build
(`MustBuildReason.UpstreamVersion`). Packages this World does not produce are out of scope — their
alignment belongs to the 'D' discrepancies mapping. This is the version-level counterpart of the
branch invariant demonstrated in [`HotZone-Workflow.md`](../HotZone-Workflow.md): that invariant
heals after the fact, and checking the profile before publishing makes it preventive.

`ToRenderable` returns the verdict, or null when there is nothing to report.

### `PublishedProfile` — the published profile

`BuildFinalProfile(monitor, roadmap, world, profileVersion)` builds a
[`CK.Packaging.Abstractions.PublishedProfile`](https://github.com/CK-Build/CK-Packaging-Abstractions/blob/stable/CK.Packaging.Abstractions/README.md) — the serializable **contract**
itself, not a plugin-private type — from the **real** content: `BuildResult` for built solutions,
version tags for the others. The set of package identifiers a solution produces is only known once
it has been built, so this is the only place a complete profile exists.

| Part of the profile | Where it comes from |
|---|---|
| `StackUrl` | `World.StackRepository.OriginUrl`. |
| `World` | `World.Name` (a `LocalWorldName`, hence a `WorldName`). |
| `Version` | `PublishedFolder.CreateNewProfileVersion` — see [The profile version](#the-profile-version). |
| `Repositories` | One `Repository` per `Repo`, keyed by `(Repo.OriginUrl, Repo.CKliRepoId)` and holding the `PackageInstance`s it **produces**. |

A package always belongs to the repository that *produces* it: a version that appears only because
another solution consumes it is recorded against its producer, never against its consumer. So a
package identifier lands in exactly one `Repository`, which is what the profile's constructor
requires.

Building the profile is also the final check. The nested `Offer` accumulator registers every
produced package identifier at its solution's version, then every World package identifier a
solution consumes at the version it consumes; a second version for one identifier is a conflict.
`Offer.Build` logs each one and returns null, so a profile never exists in a conflicting state and
the publication pushes nothing. This is what `Roadmap.PackageMapping`, a function of the package
identifier, cannot express — a single repository consuming the same package identifier in two
versions through conditional package references across target frameworks, in particular.

### The profile version

A profile's version is the version of the **publication**, not of anything it offers: several
repositories at several versions are published together, so no package version can name the set.
`PublishedFolder.CreateNewProfileVersion(branchKind, exploratoryName, isCIBuild)` mints it from the
day of the publication:

- `Major` is the year and `Minor` is the day in the year — `2026.254` is the 11th of September 2026.
- `Patch` starts at 0 and is incremented until the version is free in the folder. A file that exists
  but cannot be read counts as used, since `Save` would replace it.
- `branchKind`, `exploratoryName` and `isCIBuild` come from what is actually built
  (`roadmap.Graph.BranchName.VersionKind`, `roadmap.Graph.BranchName.ExploratoryName` and
  `roadmap.IsCIBuild`), so the version belongs to the branch that produced it — and that is what
  places its file:

| Branch built | CI | Profile version | File |
|---|---|---|---|
| root (`stable`) | no | `2026.254.0` | `Published/v2026.254.0.json` |
| root (`stable`) | yes | `2026.254.0--ci.0` | `Published/v2026.254.0--ci.0.json` |
| `alpha` | no | `2026.254.0-alpha` | `Published/alpha/v2026.254.0-alpha.json` |
| `alpha` | yes | `2026.254.0-alpha.0.ci.0` | `Published/alpha/v2026.254.0-alpha.0.ci.0.json` |
| `explo/spike` | no | `2026.254.0-0.spike` | `Published/explo/spike/v2026.254.0-0.spike.json` |

The CI number is always 0: the `Patch` is what distinguishes two publications of the same day on the
same branch, so a CI profile never collides with the non-CI one that sits beside it.

### `PublishedFolder` — where the profiles live

`PublishedFolder` (`PublishedFolder.cs`) is the mutable set of profiles stored as Json files under
`<Stack>/Published`, exposed by `PublishPlugin.PublishedFolder` and created on demand. Files are read
lazily and every modification (`Add`, `Remove`, `Deprecate`, `OnDeprecatedPackage`) stays in memory
until `Save` writes the added and updated ones and deletes the files of the removed ones.
`GetProfileFilePath` delegates to the abstraction's `PublishedProfile.GetProfilePath`, so a profile
file is always at the canonical path for its version — `LoadAll` ignores any `*.json` that is not.

Because the folder lives inside the Stack repository's working folder, the
`World.StackRepository.PushChanges` that follows a successful publication commits and pushes the new
profile. `Tests/Plugins.Tests`' `PublishedFolderTests` covers the folder on its own and
`PublishedProfileTests` covers what a real publication leaves in it.

### The publishers — the actual publish loop

`BasePublisher` (`Publisher/BasePublisher.cs`) holds the shared mechanics for one repository's
release, in this order: resolve the `GitHostingProvider`, check `HasAllArtifacts`, push the produced
packages through `PackageSender`, re-apply the `local/` tag as the final `v{version}` one, push the
tag, create a draft release, push the branch (with any deferred ref-specs), upload the asset files,
finalize the release, and finally `DestroyLocalRelease` to clean up `$Local` — unless
`KeepLocalReleaseAfterPublish` is configured (see [Configuration](#configuration)). Every failure
after the tag is pushed compensates: the draft release is deleted and the pushed tag removed.

The three concrete publishers differ only in which Git branch they push, and how:

| Publisher | Branch | Branch handling |
|---|---|---|
| `RoadmapPublisher` | Resolved from the version through the World's `BranchNamespace` (`dev/` for a CI build, the regular one otherwise). | Non-CI: pushes the regular branch and deletes the remote `dev/` branch that was just integrated. CI: ensures the regular branch is tracked if the repository is brand new. Also pushes the `+fake` base tag when the version is a prerelease or CI one. |
| `FixPublisher` | The explicit `fix/vMajor.Minor` branch of the Fix Workflow, which is not resolvable from the version. | None: no `dev/` cleanup, no defensive push, no extra tag. |
| `IndirectPublisher` | Resolved from the version like `RoadmapPublisher`. | None, deliberately: these releases belong to a branch the current operation is not working on, so touching its branches would be a side effect nobody asked for. |

`World.StackRepository.PushChanges` is called by `PublishPlugin` after a successful publication —
publishing also pushes whatever the Stack repository itself accumulated.

### `PackageSender` — routing packages to NuGet feeds

`PackageSender` (internal, `PackageSender.cs`) decides *which* NuGet feeds a given package version
should be pushed to, and does the pushing:

```csharp
public Task<bool> SendAsync( IActivityMonitor monitor, SVersion version,
                              ImmutableArray<string> packageNames, CancellationToken cancel );
```

- `PackageSender.Create(monitor, artifactHandler, branchModel, secretsStore)` reads all feeds via
  `ArtifactHandlerPlugin.GetConfiguredNuGetFeeds`.
- `SendAsync` resolves the version's `BranchName` through `BranchNamespace.FindRequired`, then
  gets or lazily builds a `Sender` for that `(BranchName.Index, isCI)` pair (an array indexed by
  `2 * branchIndex + (isCI ? 1 : 0)`, built once under a `Lock`).
- A `Sender` is the set of `NuGetFeedClient`s for feeds whose `PushCredentials` is non-null,
  `IsAPIKey` (only API-key auth is supported — a feed configured with username/password
  credentials is silently excluded), and whose `PushQualityFilter` accepts the version's
  `CSVersionKind`/CI flag (`NuGetFeed`/`PushQualityFilter` are defined and documented in
  `CKli.ArtifactHandler.Plugin`'s README). If no feed matches, this is an error (`monitor.Error`),
  not a silent no-op.
- `Sender.SendAsync` resolves each package name to its local `.nupkg` path
  (`ArtifactHandlerPlugin.LocalNuGetPath / "{package}.{version}.nupkg"`) and pushes to every
  matching client **in parallel** (`Task.WhenAll`), aggregating pass/fail.

A second, independent `Sender.Create(monitor, versionKind, ciBuild, artifactHandler, secretsStore)`
overload exists for building a one-off `Sender` without going through `PackageSender`/`BranchName`
resolution; it is not called from anywhere else in this plugin.

### `NuGetFeedClient` — talking to a single feed

`NuGetFeedClient` (`NuGetFeedClient.cs`, `IDisposable`) wraps the NuGet client SDK
(`NuGet.Protocol`, `NuGet.Packaging`, `NuGet.Credentials`) for one feed URL + API key:

```csharp
public NuGetFeedClient( string feedUrl, string apiKey, bool skipCache = true );

Task<IReadOnlyList<SVersion>?> GetVersionsAsync( IActivityLineEmitter logger, string packageId, CancellationToken = default );
Task<bool> DeleteAsync( IActivityLineEmitter logger, string packageId, SVersion version, CancellationToken = default );
Task<bool> DeleteAsync( IActivityLineEmitter logger, string packageId, IEnumerable<SVersion> versions, CancellationToken = default );
Task<bool> PushAsync( IActivityLineEmitter logger, string nupkgFilePath, bool skipDuplicate = true, CancellationToken = default );
Task<bool> PushAsync( IActivityLineEmitter logger, IEnumerable<string> nupkgFilePaths, bool skipDuplicate = true, CancellationToken = default );
```

Notable behavior:

- **Local (`file://`) feeds are handled specially**, bypassing the NuGet SDK's V3 push/delete
  APIs (which throw for file sources): versions are read straight from the expanded
  `{root}/{id}/{version}/` folder layout, deletes remove that folder, and pushes go through
  `OfflineFeedUtility.AddPackageToSource` instead of `PackageUpdateResource.Push`.
- **Remote feeds** authenticate via a private static `MicroProvider : ICredentialProvider`
  registered per-instance against `HttpHandlerResourceV3.CredentialService` — a small workaround
  since the NuGet SDK's credential plumbing is normally driven by `nuget.config`/interactive
  prompts, not a plain API key supplied in code.
- `DeleteAsync`'s doc comment calls out that whether this is a hard delete or an unlist is
  entirely server-dependent (nuget.org unlists; most private feeds like BaGet/Gitea/Nexus/Azure
  Artifacts hard-delete). Neither `Delete` overload is called anywhere in this plugin today — they
  exist as part of the client's public surface for other callers/tests.
- `skipCache` (default `true`) forces `SourceCacheContext.NoCache = true` so `GetVersionsAsync`
  always reflects the feed's actual current state — important since this client is used for
  push/duplicate-detection decisions, not just casual browsing.
- Versions returned by NuGet that don't parse as a CKli `SVersion` are skipped with a warning
  rather than failing the call.
- `LoggerAdapter` (`NuGetFeedClient.LoggerAdapter.cs`, private nested class) adapts CKli's
  `IActivityLineEmitter` (`Trace`/`Info`/`Warn`/`Error`) to the NuGet SDK's `NuGet.Common.ILogger`.

## Configuration

`CKli.Publish.Plugin` declares a single optional `<Plugins>` element of its own:

```xml
<Plugins>
  <Publish KeepLocalReleaseAfterPublish="true" />
</Plugins>
```

| Attribute | Default | Effect |
|---|---|---|
| `KeepLocalReleaseAfterPublish` | `false` | When true, `BasePublisher` skips the final `DestroyLocalRelease`, so the packages a build produced stay in the `$Local` NuGet feed after they have been published. Useful to keep experimenting with the produced artifacts, and for a test that needs a published version to remain locally available. It is **not** enabled by default for tests: `Tests/Plugins.Tests` is validated with the cleanup on (see the note below), so a test opts in through its own `pluginConfigurationEditor`. |

Everything else that governs where and how packages are pushed lives under
`CKli.ArtifactHandler.Plugin`'s
`<ArtifactHandler><NuGet><Feed .../></NuGet></ArtifactHandler>` element (feed URL,
`PushQualityFilter`, `PushCredentials` naming a secret resolved via `ISecretsStore`) — see that
plugin's README for the exact shape. Git-hosting credentials (used to create releases) are
likewise resolved through `GitRepositoryKey` / `ISecretsStore`, documented in `CKli.Core`'s
`GitHosting` folder.

## Known rough edges (from the code)

- **A fix publication leaves no profile.** `OnFixBuildAsync` publishes its `FixWorkflow` targets
  without computing a `PublishedProfile`, so nothing is added to the `PublishedFolder`: a
  `fix/vMajor.Minor` release is invisible in the profile history. Only `OnRoadmapBuildAsync` records
  one. This is deliberate for now — a fix workflow is a flat list of targets, not a dependency
  roadmap, so what its profile should offer (and on which branch its version belongs) is undecided.
- The gate's failure paths have no integration test coverage: `Tests/Plugins.Tests` never reaches
  `PublishableStatus.IndirectPublishRequired`, so the `RequiredPublications` closure, its
  producers-first ordering, `IndirectPublisher`, and `BuildFinalProfile`'s conflict branch are all
  exercised only by reasoning. Constructing that state needs a fixture with a second configured
  branch.
- `NuGetFeedClient.DeleteAsync` is fully implemented but never invoked by `PackageSender` or any
  publisher — there is currently no CKli-level command that deletes/unlists a published package
  version through this plugin.
- `BasePublisher`'s `$Local` cleanup used to be skipped by matching a hard-coded test path
  (`/.PublicStack/CK-Plugins/Tests/Plugins.Tests`). That literal never matched any real folder — CKli's
  own harness lives in `CKli-Plugins`, not `CK-Plugins` — so the cleanup always ran, including in tests,
  and the "trick for the tests" its comment described never happened. It is now driven by the
  `KeepLocalReleaseAfterPublish` configuration above, left off by default: `Tests/Plugins.Tests` has only
  ever been green with the cleanup on, and enabling it makes `S2.intermediate_build_error_Async`'s
  `ckli publish` fail. Whether that test or the intended behavior is wrong is still open.
