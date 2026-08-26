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

1. `PublishRoadmap.Create(monitor, roadmap, versionTag)` builds a `PublishRoadmap` — computing
   which solutions require **indirect** publication (see below) — and renders it to the screen.
2. Unless `roadmap.DryRun` is set, a `PackageSender` is created from the World's configured NuGet
   feeds, and `PublishRoadmap.PublishAsync` runs the actual publication.
3. On failure (`packageSender == null` or `PublishAsync` returns `false`), `e.SetFailed()` is
   called, which fails the owning command.

#### `OnFixBuildAsync` — `fix build` / `fix publish`

When `e.ShouldPublish` is true, it builds a `DirectPublisher` directly from the `FixWorkflow` and
its `BuildResult`s (a fix workflow is always a flat list of targets, never a dependency roadmap)
and runs `PublishAsync`. On success, and only for a non-CI build:

- Unless `e.KeepBranchOnSuccessfulPublish` is set, every target's local+remote+tracked
  `fix/vMajor.Minor` branch is deleted (`DeleteGitBranchMode.WithTrackedAndRemoteBranch`) — errors
  here are logged but ignored, since intermediate (halted) fix publications must keep their
  pushed branches for a retry, and only the final successful one cleans up.
- `FixWorkflow.DeleteCurrent(monitor, world)` removes the persisted fix-workflow state file.

On failure, `e.SetFailed()` is called.

### `PublishRoadmap` — resolving what a `Roadmap` needs to publish

`PublishRoadmap` (in `Roadmap/PublishRoadmap.cs`) wraps a built `Roadmap` and classifies its
publication prerequisites:

| Member | Meaning |
|---|---|
| `CanPublish` | `Status < PublishableStatus.BuildingPending` and no `IndirectBuildingAliens`. |
| `DirectBuildingAliens` / `DirectAlreadyPublished` | Solutions directly blocking (`BuildingPending`) or already done. |
| `IndirectRequiredPublications` (`ImmutableArray<RequiredPublish>`) | Upstream `local/` repo/versions (outside the current branch) that must be published *before* the roadmap itself, discovered by walking `VersionTagPlugin`'s release-info graph (producers + consumers of each `IndirectPublishRequired` solution). |
| `IndirectBuildingAliens` / `IndirectAlreadyPublished` | Same walk, but for dependencies that are still `building/` (blocking, error) or already published (unexpected, warning only). |

`RequiredPublish` is a `readonly record struct(RepoReleaseInfo Origin, RepoReleaseInfo Required)`
with an `IsProducer` helper (`Origin.AllProducers.Contains(Required)`).

`PublishRoadmap.Create` only does this graph walk when
`roadmap.PublishableStatus == PublishableStatus.IndirectPublishRequired`; otherwise
`IndirectRequiredPublications` is empty. **`PublishAsync` currently throws
`NotImplementedException` if any indirect required publications are found** — publishing a
roadmap whose upstream dependencies are on other branches and not yet published is not yet
implemented; only the direct case (`roadmap.DirectPublishCount > 0`, i.e. `Build` or
`PublishRequired` solutions) is handled by delegating to `DirectPublisher.Create(monitor, roadmap)`.
`ToRenderable` is currently a stub that returns `screen.Unit` (no roadmap summary is actually
rendered yet).

`PublishRepoInfo` (`Roadmap/PublishRepoInfo.cs`) is a thin, seemingly unfinished wrapper around a
`Roadmap.BuildSolution` exposing only `Repo`; it isn't referenced elsewhere in this plugin.

### `DirectPublisher` — the actual publish loop

`DirectPublisher` (partial class split across `DirectPublisher.cs`, `.RepoInfo.cs`, `.Cursor.cs`,
`.Publish.cs`) is the workhorse: it flattens "what needs publishing" into an ordered, resumable
sequence of steps and drives them one by one.

**Construction.** Two factory methods build the list of `RepoInfo` to publish:

- `Create(FixWorkflow fixWorkflow, ImmutableArray<BuildResult> results)` — one `RepoInfo` per
  fix target, branch name taken from the workflow's targets, no branch-push ref-specs.
- `Create(IActivityMonitor monitor, Roadmap roadmap)` — one `RepoInfo` per solution whose
  `PublishableStatus` is `Build` or `PublishRequired`, in `roadmap.OrderedSolutions` order. For
  each solution it picks the version/tag/content either from the just-completed `BuildResult`
  (`s.MustBuild`) or from `s.LastBuild` (already built, publish-only), and computes the branch to
  push and the `BranchPushRefSpecs`:
  - CI build (`roadmap.IsCIBuild`): pushes the `dev/` branch; if the regular branch has no
    tracked remote yet (brand-new repo), it also configures and pushes it.
  - Non-CI build: pushes the regular branch and includes a ref-spec that deletes the remote
    `dev/` branch (`:refs/heads/dev/...`) that was just integrated.

**`RepoInfo`** (`DirectPublisher.RepoInfo.cs`) carries, per repository: `Repo`, `BranchName`,
`Index`, `PublishVersion` (`SVersion`), `PublishTag` (`LibGit2Sharp.Tag`), `BuildContentInfo`
(the produced `.nupkg` names + asset file names), `BranchPushRefSpecs`, and a computed
`PublishedLength` = `1 (start) + packages + assets + 1 (end)` — the number of logical steps this
repo contributes.

**`Cursor`** (`DirectPublisher.Cursor.cs`) is an immutable position in the publish sequence, one
of `BegOfRepo → InPackage[0..n) → InFile[0..m) → EndOfRepo → … → EndOfWorld → EndOfState`. It
supports `Forward()` (one step) and `Forward(offset)` (many steps at once — used to skip an empty
package/file range), plus `GetPosition()` for progress reporting. `DirectPublisher.PrimaryCursor`
holds the live cursor; `ForwardPrimaryCursor` advances it.

**`Publisher` (`DirectPublisher.Publish.cs`)** drives the cursor through a state machine
(`RunAsync`), one repo at a time:

| Cursor location | Action |
|---|---|
| `BegOfRepo` | Resolves the `GitHostingProvider` for the repo (`RepositoryKey.TryGetHostingInfo`). If there are no packages to push, creates the release immediately; otherwise defers to `InPackage`. |
| `InPackage` | `PackageSender.SendAsync` pushes all of `BuildContentInfo.Produced` to the configured feeds, then creates the release. |
| *(release creation)* | Re-applies the tag locally if the version `IsLocal()` (fake/dirty tag replaced with the final `v{version}` annotated tag), pushes the tag, calls `GitHostingProvider.CreateDraftReleaseAsync`, then pushes the branch (`GitRepository.PushBranch`, `autoCreateRemoteBranch: true`) together with the `DeferredPushRefSpecs`. If the branch push fails, the draft release is deleted (compensation); if the release itself couldn't be created, the pushed tag is removed from `origin` (best-effort compensation, logged if it fails). |
| `InFile` | For each produced asset file, `GitHostingProvider.AddReleaseAssetsAsync` uploads the assets folder returned by `ArtifactHandlerPlugin.GetAssetsFolder`. |
| `EndOfRepo` | `GitHostingProvider.FinalizeReleaseAsync` (un-drafts the release), then `ArtifactHandlerPlugin.DestroyLocalRelease` cleans up the local `$Local` copy (skipped under the plugin's own test path, to keep produced versions around for test assertions). |
| `EndOfWorld` | Logs completion. |

After the whole cursor reaches `EndOfState`, `RunAsync` finishes by calling
`World.StackRepository.PushChanges` — publication also pushes whatever the Stack repository
itself accumulated (World file edits, version-tag bookkeeping, etc.).

Any step returning `null` aborts the whole run and `PublishAsync` returns `false`.

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

- `PublishRoadmap.PublishAsync` throws `NotImplementedException` when a roadmap has any
  `IndirectRequiredPublications` — publishing across branches when an upstream dependency on
  another branch hasn't been published yet is detected but not yet actually performed.
- `PublishRoadmap.ToRenderable` is a stub (`screen.Unit`): no publish roadmap summary is rendered
  to the screen yet, despite `PublishPlugin.OnRoadmapBuildAsync` calling `e.Screen.Display(...)`
  with it.
- `PublishRepoInfo` (`Roadmap/PublishRepoInfo.cs`) is minimal (`Repo` only) and unused elsewhere in
  the plugin — it looks like a placeholder for future per-repo roadmap-publish reporting.
- `NuGetFeedClient.DeleteAsync` is fully implemented but never invoked by `PackageSender` or
  `DirectPublisher` — there is currently no CKli-level command that deletes/unlists a published
  package version through this plugin.
