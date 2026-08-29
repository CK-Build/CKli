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
| `CKli.BranchModel.Plugin` | `BranchNamespace` — maps a version's `CSVersionKind` to the `BranchName` (and its `Index`) that owns it, used to pick which configured feeds/senders apply. |
| `CKli.VersionTag.Plugin` | `EnsureDatabase` / release-info graph used to resolve indirect publication requirements (see caveat below). |
| `CKli.HotZone.Plugin` | Indirectly, through `FixWorkflow` (owned by `HotZone`) which `CKli.Build.Plugin` passes along in `OnFixBuild`. |

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
   is created from the World's configured NuGet feeds, and `PublishRoadmap.PublishAsync` runs the
   actual publication with a `RoadmapPublisher` and an `IndirectPublisher`.
3. On failure (closed gate, `packageSender == null`, or `PublishAsync` returning `false`),
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
| `FinalProfile` (`PublishedProfile?`) | The profile this publication offers. Available once `PublishAsync` has run. |
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

`BuildFinalProfile(monitor, roadmap)` builds the profile from the **real** content: `BuildResult`
for built solutions, version tags for the others. The set of package identifiers a solution produces
is only known once it has been built, so this is the only place a complete profile exists.

Building it is also the final check: a conflict means the publication would offer two versions of
one package identifier — which `Roadmap.PackageMapping`, a function of the package identifier,
cannot express (a single repository consuming the same package identifier in two versions through
conditional package references across target frameworks, in particular). `PublishedProfile.Builder`
returns null in that case, so a profile never exists in a conflicting state.

Each entry is a `PublishedPackageInfo : PackageInstance` carrying its `Reason`s
(`PublishedByRoadmap` / `RequiredBySolution`) and any `Conflicts`.

### The publishers — the actual publish loop

`BasePublisher` (`Publisher/BasePublisher.cs`) holds the shared mechanics for one repository's
release, in this order: resolve the `GitHostingProvider`, check `HasAllArtifacts`, push the produced
packages through `PackageSender`, re-apply the `local/` tag as the final `v{version}` one, push the
tag, create a draft release, push the branch (with any deferred ref-specs), upload the asset files,
finalize the release, and finally `DestroyLocalRelease` to clean up `$Local`. Every failure after
the tag is pushed compensates: the draft release is deleted and the pushed tag removed.

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

`CKli.Publish.Plugin` declares **no `<Plugins>` XML element of its own**. All configuration that
governs where and how packages are pushed lives under `CKli.ArtifactHandler.Plugin`'s
`<ArtifactHandler><NuGet><Feed .../></NuGet></ArtifactHandler>` element (feed URL,
`PushQualityFilter`, `PushCredentials` naming a secret resolved via `ISecretsStore`) — see that
plugin's README for the exact shape. Git-hosting credentials (used to create releases) are
likewise resolved through `GitRepositoryKey` / `ISecretsStore`, documented in `CKli.Core`'s
`GitHosting` folder.

## Known rough edges (from the code)

- The gate's failure paths have no integration test coverage: `Tests/Plugins.Tests` never reaches
  `PublishableStatus.IndirectPublishRequired`, so the `RequiredPublications` closure, its
  producers-first ordering, `IndirectPublisher`, and `BuildFinalProfile`'s conflict branch are all
  exercised only by reasoning. Constructing that state needs a fixture with a second configured
  branch.
- `NuGetFeedClient.DeleteAsync` is fully implemented but never invoked by `PackageSender` or any
  publisher — there is currently no CKli-level command that deletes/unlists a published package
  version through this plugin.
- `BasePublisher`'s `$Local` cleanup is skipped under a hard-coded test path
  (`/.PublicStack/CK-Plugins/Tests/Plugins.Tests`), so it applies to one stack's harness only.
