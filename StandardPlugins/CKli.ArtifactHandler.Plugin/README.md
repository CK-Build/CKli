# CKli.ArtifactHandler.Plugin

**CKli.ArtifactHandler.Plugin** manages where build artifacts (NuGet packages and arbitrary release assets) live locally, and keeps each repository's `nuget.config` file synchronized with the World's configured NuGet feeds.

It is a *service* plugin: unlike most Standard Plugins, it exposes no `[CommandPath]` commands of its own. Instead it is consumed programmatically by the plugins that actually build, tag and publish repositories:

| Consumer | Uses it for |
|---|---|
| `CKli.Build.Plugin` (`RepoBuilder`, `RepositoryBuilderPlugin`) | After a repository build, calls `PublishToNuGetLocalFeed` and `PublishGeneratedAssets` to move the produced `.nupkg`s and `Deployment/Assets` files into the local feed/assets folders. |
| `CKli.VersionTag.Plugin` | Reads/writes the `BuildContentInfo` text form to/from version tag annotations, calls `HasAllArtifacts` to decide whether a tagged version needs rebuilding, and `DestroyLocalRelease` when a release is deprecated. |
| `CKli.Publish.Plugin` (`PackageSender`, `BasePublisher`) | Reads `GetConfiguredNuGetFeeds` to know where and with which credentials to push packages, reads assets via `GetAssetsFolder`, and calls `DestroyLocalRelease` once a release has been fully published. |

So while `CKli.VersionTag.Plugin` owns the release/tag lifecycle and `CKli.Publish.Plugin` owns pushing to remote feeds/releases, `CKli.ArtifactHandler.Plugin` is the shared layer underneath both (and under `CKli.Build.Plugin`) that owns the local artifact storage and the NuGet feed configuration.

## Why

Building a repository produces `.nupkg` files and, optionally, other release assets (zips, binaries...). These artifacts need to:

- Be collected into a well-known **local NuGet feed** so that other repositories in the World can immediately consume the packages they just produced, without waiting for a remote push.
- Be tracked per **repository + version** so a later step (or the `VersionTag` plugin) can tell whether a given tagged version has already been fully built, or is missing some artifacts and must be rebuilt.
- Have their identity (which packages, which asset files) recorded durably: this plugin defines the exact text format that is stored **inside the annotated Git tag** created for a release.
- Have every repository's `nuget.config` point at the right feeds — including feeds that require an API key to push, and "fake" read credentials for feeds that require authentication even for anonymous/public reads (e.g. GitHub Packages).

Centralizing this in one plugin means the local feed layout, the NuGet feed configuration format, and the tag-content serialization format are defined once and shared by every plugin that produces or consumes artifacts.

## How

### Plugin shape

`ArtifactHandlerPlugin` derives from `PrimaryRepoPlugin<RepoArtifactInfo>` (see `CKli.Core`'s Plugin System): it is always instantiated for the World, and it lazily creates one `RepoArtifactInfo` per `Repo` on demand (`Create(monitor, repo)`).

It takes a `BranchModelPlugin` dependency in its constructor (constructor injection, resolved by CKli) and subscribes to its `ContentIssue` event:

```csharp
public ArtifactHandlerPlugin( PrimaryPluginContext context, BranchModelPlugin branchModel )
    : base( context )
{
    ...
    branchModel.ContentIssue += HandleNuGetConfig;
}
```

`HandleNuGetConfig` runs for every content-managed branch and ensures a `nuget.config` file exists and is up to date: it creates the default one if missing, or diffs/patches an existing one (package sources, source mapping, and `<packageSourceCredentials>`) against the World's configured feeds — reporting each drift as an issue action.

### Local storage layout

Two folders are created under the World's local data folder (`$Local/<World>/...`, never committed to Git):

| Property | Path | Purpose |
|---|---|---|
| `LocalNuGetPath` | `$Local/<World>/NuGet` | Local NuGet feed: every produced `.nupkg` lands here. |
| `LocalAssetsPath` | `$Local/<World>/Assets` | Release assets, organized as `Assets/<repo>/<version>/`. |

`RepoArtifactInfo` (the per-repository, lazily-created `RepoInfo`) exposes the two operations that populate these folders after a build:

- `PublishToNuGetLocalFeed(monitor, version, buildOutputPath, out packageIdentifiers)` — moves every `.nupkg` produced in a build output folder into the local feed, validating the `PackageName.Version.nupkg` naming convention, and evicts the package from the global NuGet cache (`%userprofile%\.nuget\packages`) first so `dotnet restore` doesn't keep resolving a stale cached copy.
- `PublishGeneratedAssets(monitor, version, out assetsFolder, out fileNames)` — copies files from the repository's `Deployment/Assets/` folder (`ArtifactHandlerPlugin.DeployFolderName` / `DeployAssetsName`) into `Assets/<repo>/<version>/`, zipping any subdirectories found there.

`ArtifactHandlerPlugin.HasAllArtifacts(monitor, repo, version, buildContentInfo, out assetsFolder)` is the read-side check: given a previously recorded `BuildContentInfo`, it verifies every produced package and every asset file is still present locally — this is what lets a caller skip rebuilding a version that is already fully available. `DestroyLocalRelease(...)` is the inverse: it removes a release's packages (optionally purging the global NuGet cache too) and its assets folder; the doc comment notes it "should only be called by the VersionTag plugin".

### Recording what a build produced — `BuildContentInfo` / `BuildResult`

`BuildContentInfo` is the durable, serializable description of one build: the packages it **consumed** (`ImmutableArray<PackageInstance>`), the packages it **produced**, and the asset file names it generated. It supports both binary serialization (`ICKBinaryReader`/`ICKBinaryWriter`) and a round-trippable text format (`Write(StringBuilder)` / `TryParse(...)`) — this text form is what gets stored as the **annotation message of the version's Git tag** (`refs/tags/vX.Y.Z`), so the release's contents can be recovered later just by reading the tag, without needing to rebuild.

`BuildResult` wraps a completed (or skipped) build: the `Repo`, the resolved `SVersion`, the `Tag`, the `BuildContentInfo`, and the local `AssetsFolder`. It enforces the tag-naming convention for the three version states a build can be in:

- `refs/tags/building/vX.Y.Z` — a build in progress.
- `refs/tags/local/X.Y.Z` — a build that completed locally but wasn't (yet) published.
- `refs/tags/vX.Y.Z` — a fully published version.

`CommitBuilding()` performs the `building/` → `local/` tag transition once a build succeeds. `GetConsumedPackages(monitor, repo, buildInfo, out packages)` shells out to `dotnet package list --format json --no-restore` (deliberately chosen over parsing `project.assets.json` or using Buildalyzer — see the doc comment — for robustness across multi-project solutions) to discover the top-level NuGet dependencies a repository actually consumed for that build.

### NuGet feed configuration — `NuGetFeed` / `NuGetFeedCredentials`

`GetConfiguredNuGetFeeds(monitor, out feeds)` reads the `<ArtifactHandler><NuGet><Feed .../></NuGet></ArtifactHandler>` element from the World's plugin configuration (`PrimaryPluginContext.Configuration`, an `XElement`) into a `NuGetFeed[]`, and — the first time it's read with zero feeds configured — self-initializes the configuration with `https://api.nuget.org/v3/index.json` as a default push target, so a World never ends up with no feed at all.

```xml
<ArtifactHandler>
  <NuGet>
    <Feed Name="NuGet" Url="https://api.nuget.org/v3/index.json" PushQualityFilter="[,].ci">
      <PushCredentials SecretKey="NUGET_ORG_PUSH_API_KEY" />
    </Feed>
  </NuGet>
</ArtifactHandler>
```

Each `NuGetFeed` carries:

- `PushCredentials` — an optional `NuGetFeedCredentials` naming the secret (resolved through `ISecretsStore`, see `CKli.Core`) required to push. No `PushCredentials` means CKli will never push to that feed.
- `PushQualityFilter` — a `CSVersionKindFilter` restricting which version kinds (CI, prerelease, stable...) may be pushed to the feed.
- `FakeReadCredentials` — an optional username/password pair written verbatim into the repository's `nuget.config` `<packageSourceCredentials>`, working around hosts (GitHub Packages is called out explicitly) that require authentication even to anonymously read a public feed.

`ApplyConfiguredNuGetFeeds(monitor, root, out actions)` and `GetDefaultNuGetConfig(monitor)` turn this feed list into (or reconcile it against) an actual `nuget.config` `<configuration>` XML document — sources, package source mapping (`*` pattern per source), and credentials — which is exactly what `HandleNuGetConfig` uses to keep every repository's `nuget.config` file correct.

### Other helpers

- `SVersionExtensions` adds `IsBuilding()` / `IsLocal()` / `IsBuildingOrLocal()` predicates over `SVersion.ParsedPrefix`, and a monitor-logging variant of `IsPreviousVersionNumbersOf`.
- `XNames` centralizes the `XName`s used for the `<ArtifactHandler>` configuration XML.

## Dependencies

- **`CKli.BranchModel.Plugin`** (project reference): supplies the `ContentIssue` event this plugin hooks to manage `nuget.config` files across all content-managed branches.
- **`NuGet.Protocol`** (NuGet package): used indirectly through `NuGetHelper` (from `CKli.Core`) for cache and configuration operations.
