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
| `CKli.VersionTag.Plugin` | `EnsureDatabase` / release-info graph used to resolve indirect publication requirements (see caveat below), and the source of the `VersionDeprecated` event. |
| `CKli.HotZone.Plugin` | Indirectly, through `FixWorkflow` (owned by `HotZone`) which `CKli.Build.Plugin` passes along in `OnFixBuild`. |
| `CK.Packaging.Abstractions` | The [`PublishedProfile`](https://github.com/CK-Build/CK-Packaging-Abstractions/blob/stable/CK.Packaging.Abstractions/README.md) contract: the immutable, serializable description of what a publication carries. It arrives transitively through `CKli.Core`. |

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
- `_versionTag.VersionDeprecated.Sync += OnVersionDeprecated`

It defines no `[CommandPath]` methods and does not subscribe to `World.Events` — all of its work
is triggered from these three events.

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
3. On success, the `FinalProfile` is added to the `PublishedFolder`, the CI profiles it supersedes are
   removed (see [Superseded CI profiles](#superseded-ci-profiles)) and the folder is `Save`d, then
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

On success, `OnFixedProfiles` supersedes the published profiles the fix invalidates. A fix publishes
versions that replace the ones it fixes, and older profiles still *carry* those:

1. The fixed packages are `results[i].Content.Produced` at `TargetRepo.ToFixVersion`, superseded by
   `TargetRepo.TargetVersion`. That set is exact because `BuildPlugin.Fix` **forbids** a fix from
   changing its produced package identifiers — the fix's output is the same package set as the version
   it fixes.
2. `PublishedFolder.OnFixedPackages` adds, beside every profile that carries one of them, a profile with
   the same produced packages and the fixed versions replaced.

Three rules govern it, and they are what the method's shape is for:

| Rule | Why |
|---|---|
| The superseded profile is **left untouched**. | A profile records what was actually published; a fix does not change the past. The successor sits beside it. |
| The successor keeps its origin's `Major.Minor` and branch, with the next free `Patch` — "as if built the same day". | The publication it describes never happened on its own day; it belongs with the profile it corrects. `CreateSupersedingProfileVersion` does this. |
| A **deprecated profile is locked** and gets no successor. | It is dead: deprecation is monotonic and there is no un-deprecate, so a successor would resurrect produced packages nobody should pick up. |

`fixedPackages` is keyed by `PackageInstance` — the package identifier **and** the version being fixed —
rather than by identifier alone, because **one fix publication can target two Major.Minor lines of the
same repository**. `S1`'s `local_fix_Async` does exactly that: a single workflow carries
`CKt-PerfectEvent ⎇ fix/v0.2 → v0.2.2` *and* `CKt-PerfectEvent ⎇ fix/v0.3 → v0.3.3`, so `CKt.PerfectEvent`
is superseded from `0.2.1` and from `0.3.2` in the same pass. Keyed by identifier those would collide;
keyed by instance each profile picks up the entry matching the version it actually carries.

Within one profile a `Repository` carries a single version across all its packages — a `Repo` has exactly
one solution (`HotZonePlugin` adds one per repo) and a commit bears at most one version — and the fix's
produced identifiers are exactly the fixed version's (`BuildPlugin.Fix` enforces that). So when a
`Repository` is superseded, *all* of its packages move together; the per-package loop is how the
instance-keyed map is consumed, not a way to move only some of them.

The successor also carries its origin's `DirectDependencies` and `TransitiveDependencies`: a fix
successor is a projection of its origin and `OnFixBuildAsync` has no roadmap to recompute them from.
They can therefore be **stale** — a fix that changed an external reference is not reflected — which is
acceptable only because a fix is a minimal patch on a released line. The one thing that cannot be
carried verbatim is an ambiguity anchored on a produced package whose version the fix moves: its anchor
moves with it, and a resolution the superseding version has caught up with stops being a disagreement,
so such an ambiguity shrinks or disappears. `SameProducedPackages` still compares produced packages
only, so none of this affects idempotency.

It is idempotent. A retry of an interrupted fix publication finds that some profile already carries the
resulting produced packages — `OnFixedPackages` compares produced packages, not versions — and adds
nothing. As with the other `PublishedFolder` writers, the Stack is then committed with a message naming
the reason and pushed, and a failure to write is logged rather than failing a publication that cannot
be undone.

#### `OnVersionDeprecated` — `version deprecate`

The deprecation of a version and the deprecation of a profile are the same fact seen from the two
sides of the publication: a deprecated version is still *carried* by every profile that was published
with it. `CKli.VersionTag.Plugin` owns the propagation across versions — deprecating a version
deprecates every release that consumes it, transitively — and raises
[`VersionDeprecated`](../CKli.VersionTag.Plugin/README.md#versiondeprecated--the-extension-point-this-plugin-offers)
once every `+deprecated` tag is pushed. This handler mirrors the **whole** result onto the profiles,
which is why the event carries every deprecated release and not only the one the command named:

1. For each of `e.DeprecatedPackages`, and depending on `e.HasExpired`:

   | `HasExpired` | What it means | What the profile gets |
   |---|---|---|
   | false | The deprecation is still to come (`--days <n>`). | `OnDeprecatedPackage` — the profile is **marked**. |
   | true | The version tag is gone and the packages must leave the feeds (`--immediate`, or a date now past). | `OnExpiredPackage` — the profile is **deleted**. |

   Either way a profile is touched only when it carries *exactly* that package at that version, so a
   later profile that carries the same package identifiers in newer versions is left alone.
2. Any file in `LoadErrors` is warned about: it could not be considered at all, so it may still carry
   a deprecated package.
3. If nothing changed, it stops — that is the normal case for a Stack with no publication yet.
   Otherwise `Save` writes (or deletes) the touched files, the Stack is committed with a message that
   names the reason, and `PushChanges` pushes it. The commit is explicit on purpose: `PushChanges`
   alone would record profiles disappearing under "Automatic pre-push commit.".

It takes the event's `Sync` slot: the work is file IO and git, so there is nothing to await. That is a
choice this plugin makes for itself — `VersionDeprecated` is a `PerfectEvent`, so another listener can
take the `Async` or `ParallelAsync` slot without affecting this one.

Both outcomes are idempotent, which is what makes a failed handler safe to retry: `Deprecate` is a
no-op on an already deprecated profile (and there is no un-deprecate), and an expired package finds no
profile left to remove.

**A deprecated profile is locked.** It records what was published and it will never gain a successor:
when the fix workflow starts writing updated profiles, a deprecated one is not a candidate.

### `PublishRoadmap` — the gate and the publish loop

`PublishRoadmap` (in `Roadmap/PublishRoadmap.cs`) wraps a `Roadmap` with its publication gate:

| Member | Meaning |
|---|---|
| `Gate` (`PublishedProfileBuilder`) | Whether the profile this publication would leave on the roadmap's branch is coherent, and what must happen for it to be. |
| `CanPublish` | `Status < PublishableStatus.BuildingPending` and `Gate.IsValid`. |
| `FinalProfile` (`PublishedProfile?`) | The profile this publication carries. Available once `PublishAsync` has run, and on success this is what `PublishPlugin` stores in the `PublishedFolder`. |
| `DirectBuildingAliens` / `DirectAlreadyPublished` | Solutions directly blocking (`BuildingPending`) or already done. |

`PublishAsync` runs three steps, and pushes nothing until all of them are satisfied:

1. `Gate.BuildFinalProfile` builds the profile from the real build and tag content. A conflict here
   aborts the publication.
2. Every `Gate.RequiredPublications` release is published by the `IndirectPublisher`, producers
   first.
3. Every solution whose `PublishableStatus` is `Build` or `PublishRequired` is published by the
   `RoadmapPublisher`, in `OrderedSolutions` order.

### `PublishedProfileBuilder` — the publication gate

A World's **published profile** on a branch is the set of packages it carries there: exactly one
version per package identifier, with every version required by one of them being the one the
profile carries. `PublishedProfileBuilder` (`Roadmap/PublishedProfileBuilder.cs`) decides whether
publishing a roadmap would leave that profile coherent.

The gate works at the **solution** level. Every package a solution produces carries that solution's
single version, and a package identifier is produced by exactly one solution
(`HotGraph.ProducedPackages`), so the version a package carries is a function of its producing
solution — `Roadmap.BuildSolution.TargetVersion`. Package identifiers are only the join key: the
version a consumer was built against comes from its recorded `BuildContentInfo.Consumed` and is
mapped back to the solution that produces it.

`Create(monitor, roadmap, versionTag)` runs **before any build**, so a `--dry-run` gets the same
verdict as a real publication:

| Member | Meaning |
|---|---|
| `IsValid` | No `Discrepancies`, no `BuildingAliens`, no `MissingArtifacts`. |
| `Discrepancies` (`ImmutableArray<Discrepancy>`) | `(Consumer, Producer, Required, Produced)`: a solution that is not built recorded a requirement that disagrees with the version its producer will produce. Only solutions that are *not* built can disagree — a built solution has its World references rewritten from the `Roadmap.PackageMapping`, which is those very versions. |
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
| `DirectDependencies` | Every `BuildContentInfo.Consumed` package that the final produced index does not carry. |
| `TransitiveDependencies` | The union of every `BuildContentInfo.Transitive` — NuGet's own resolution — classified against the two sets above. |

A package always belongs to the repository that *produces* it: a version that appears only because
another solution consumes it is recorded against its producer, never against its consumer. So a
package identifier lands in exactly one `Repository`, which is what the profile's constructor
requires.

Building the profile is also the final check. The nested `ProducedPackages` accumulator registers
every produced package identifier at its solution's version, then every World package identifier a
solution consumes at the version it consumes; a second version for one identifier is a conflict.
`ProducedPackages.Build` logs each one and returns null, so a profile never exists in a conflicting
state and the publication pushes nothing. This is what `Roadmap.PackageMapping`, a function of the
package identifier, cannot express — a single repository consuming the same package identifier in
two versions through conditional package references across target frameworks, in particular.

#### What the repositories consume

Beyond what they produce, the profile records what its repositories **consume**.

`DirectDependencies` is every `BuildContentInfo.Consumed` package that this publication does not
produce, accumulated by the nested class of the same name. Its filter is the **final produced index**,
never `roadmap.Graph.ProducedPackages`: the graph only sees a project as packable once something in
the stack references it, so it under approximates what a solution produces, and a stale reference in a
frozen `Consumed` would then make a produced identifier look external — which the profile's constructor
rejects with an `ArgumentException`. One extra pass over the solutions makes the invariant hold by
construction.

That set is expected to be coherent — one version per identifier — because the 'D' discrepancies
mapping aligns every clashing external reference onto the greatest one and a solution that disagrees is
forced to build. So its conflict branch is an assertion with a message rather than a gate, and the
message names **both** colliding repositories and versions: the 'D' guarantee runs through the *shallow*
read of the project files while this set comes from the MSBuild evaluated `Consumed`, and the two can
diverge — a repository this publication did not rebuild keeps a frozen `Consumed`, and a genuine NuGet
version range resolves to something other than the project's text.

`TransitiveDependencies` is what a restore brings beyond those: the union of every repository's
`BuildContentInfo.Transitive`, which is NuGet's own answer rather than a computed closure (see
[the ArtifactHandler plugin](../CKli.ArtifactHandler.Plugin/README.md#recording-what-a-build-produced--buildcontentinfo--buildresult)). The nested
`TransitiveDependencyUnion` puts those answers side by side and classifies each identifier:

- **already carried** by `ProducedPackages` or `DirectDependencies` — only a resolution *greater* than
  that entry is recorded, as an ambiguity anchored on it. A smaller one is invisible to a restore, and
  "an identifier is both stated and transitively resolved" is the common case: burying the list with it
  would make it useless.
- otherwise **one** resolved version — a `Regular` entry; **several** — an ambiguity resolved from the
  transitive packages themselves, at the greatest of them (NuGet's highest-wins across the packages a
  consumer takes together).

A `VersionResolution` names the repositories that resolved it, by their `Repo.CKliRepoId`. Two of them
contribute nothing at all: a repository that produces no package has no `Repository` entry in the
profile, so a resolution could not name it; and a repository whose build predates the
`--include-transitive` capture has no set — `HasTransitive` false means *unknown*, which is not *empty*.

Disagreement here is expected rather than exceptional: the 'D' mapping aligns the **direct** external
references across repositories, not their transitive resolutions. Two repositories whose graphs differ
legitimately land on two versions of a package neither of them references, and a single repository does
it alone when two of its target frameworks resolve differently. These are external packages nobody here
references, so they are reported with a warning and never gated.

### The profile version

A profile's version is the version of the **publication**, not of anything it carries: several
repositories at several versions are published together, so no package version can name the set.
`PublishedFolder.CreateNewProfileVersion(branchKind, exploratoryName, isCIBuild)` mints it from the
day of the publication:

- `Major` is the year and `Minor` is the day in the year — `2026.254` is the 11th of September 2026.
- `Patch` starts at 0 and is minted **above every version already used that day on that branch** -
  never in a hole. A file that exists but cannot be read counts as used, since `Save` would replace it.
  The regular and the CI form of a branch **share** that counter (they have the same
  `SVersion.BranchName`): a number is never reused, so a version keeps identifying one publication even
  though a superseded CI profile gets deleted - see [Superseded CI profiles](#superseded-ci-profiles).
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

### Superseded CI profiles

Every CI build publishes a profile, so without pruning a folder would grow by one file per CI build -
and the index is fetched by whoever reads the World from outside. `PublishedFolder.RemoveSupersededCIProfiles(publishedVersion)`
is called by `PublishPlugin` between the `Add` and the `Save` of a publication:

| The publication | Removes |
|---|---|
| a CI one | the CI profiles that **precede** it on its own branch |
| a non-CI one | **every** CI profile of its own branch |

Only the alive profiles are considered - a `IsDeprecated` one is a record of a problem and is left
alone - non-CI profiles are never removed, and another branch is never touched. Deleting rather than
keeping is what `OnExpiredPackage` already does, and for the same reason: a superseded CI publication
describes a state that no longer applies, whose packages the CI feeds eventually unlist. The folder is
in the Stack repository, so a removed profile stays in its history.

The invariant this leaves is what makes a reader simple: **at most one alive CI profile per branch, and
it is newer than every non-CI publication of that branch**. A consumer that wants the CI line can take
the CI profile it finds in the index at face value, with no "is it superseded" arithmetic. Note that
pruning never removes the maximum version, which is what keeps the shared `Patch` counter above
(a freed number is never reused) derivable from the folder itself.

### `PublishedFolder` — where the profiles live

`PublishedFolder` (`PublishedFolder.cs`) is the mutable set of profiles stored as Json files under the
World's `LocalWorldName.SharedDataFolder` — `.PublicStack/Published` for the default World and
`.PublicStack/{LTSName}/Published` for a Long Term Support one — exposed by
`PublishPlugin.PublishedFolder` and created on demand. Files are read
lazily and every modification (`Add`, `Remove`, `Deprecate`, `OnDeprecatedPackage`,
`RemoveSupersededCIProfiles`) stays in memory
until `Save` writes the added and updated ones and deletes the files of the removed ones.
`GetProfileFilePath` delegates to the abstraction's `PublishedProfile.GetProfilePath`, so a profile
file is always at the canonical path for its version — `LoadAll` ignores any `*.json` that is not.

It is **World scoped, not Stack scoped**: the Worlds of a Stack publish independently, so their profiles
share neither a folder, nor an index, nor the next free `Version` Patch of the day. That is the same
`SharedDataFolder` convention the plugin solution (`{SharedDataFolder}/{World}-Plugins`) and the
CommonFiles folder (`{SharedDataFolder}/Common`) already follow — and a consumer reading a reference's
published index therefore reads `{LTSName}/Published/index.json` for an LTS World.

Because the folder lives inside the Stack repository's working folder, the
`World.StackRepository.PushChanges` that follows a successful publication commits and pushes the new
profile. `Tests/Plugins.Tests`' `PublishedFolderTests` covers the folder on its own and
`PublishedProfileTests` covers what a real publication leaves in it and what a `version deprecate`
does to it.

Three entry points write to it: `OnRoadmapBuildAsync` adds a profile and removes the CI ones it
supersedes,
[`OnVersionDeprecated`](#onversiondeprecated--version-deprecate) marks the ones that carry a deprecated
package — or removes them, once that deprecation has expired — and
[`OnFixBuildAsync`](#onfixbuildasync--fix-build--fix-publish) supersedes the ones a fix invalidates.
`OnDeprecatedPackage`, `OnExpiredPackage` and `OnFixedPackages` are the package-oriented mutators that
back those, and all three read every file.

#### `index.json` — the folder's own reflection

`Save` refreshes `Published/index.json` whenever at least one profile file actually changed (that file is
not counted in what it returns). `CreateIndex` is what it writes: a
[`PublishedIndex`](https://github.com/CK-Build/CK-Packaging-Abstractions/blob/stable/CK.Packaging.Abstractions/README.md#publishedindex)
of every profile this folder holds, which is the abstraction's type — the format, the group names and
the ordering are defined there, once, and this folder only decides *when* to write it and *where*.

That split is the point: the index is written here and read by **whoever looks at this folder from
the outside**, including another World, with `PublishedIndex.Parse` and without this plugin.

- **The index is a reflection, never a source.** `PublishedFolder` writes it and never reads it back:
  `LoadAll` only accepts a file whose whole name is a version, and `index` is not one. Delete it and the
  next `Save` that changes something puts it back; nothing else notices.
- Since the grouping is a pure function of the versions, a hand-edited index cannot quietly survive:
  `PublishedIndex.Read` checks each group name against the versions it holds.
- `IndexFileName` here is `PublishedIndex.IndexFileName`, and `IndexFilePath` is that name at the
  `RootPath`.

The suffix that separates a branch's CI builds from its regular versions (`alpha-ci`, `explo/spike-ci`,
`(stable-ci)`) cannot collide with a branch name, and that guarantee is enforced on this side: an
exploratory name that ends with `-ci` — or starts with `ci-` — is refused by
`SVersion.IsReservedExploratoryName`, applied on the version side (`SetExploratoryName` and the parser)
and on the branch side (`branch open`, `AddOrUpdateExplo` and the BranchModel configuration).

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
- A `Sender` is the set of `NuGetFeedClient`s for feeds whose `Credentials` is non-null (which is
  always an API key: `NuGetFeed`'s constructor enforces it) and whose `PushQualityFilter` accepts the version's
  `CSVersionKind`/CI flag (`NuGetFeed`/`PushQualityFilter` are defined and documented in
  `CKli.ArtifactHandler.Plugin`'s README). If no feed matches, this is an error (`monitor.Error`),
  not a silent no-op.
- `Sender.SendAsync` resolves each package name to its local `.nupkg` path
  (`ArtifactHandlerPlugin.LocalNuGetPath / "{package}.{version}.nupkg"`) and pushes to every
  matching client **in parallel** (`Task.WhenAll`), aggregating pass/fail.

A second, independent `Sender.Create(monitor, versionKind, ciBuild, artifactHandler, secretsStore)`
overload exists for building a one-off `Sender` without going through `PackageSender`/`BranchName`
resolution; it is not called from anywhere else in this plugin.

### The feed clients

`NuGetFeedClient` and its `LoggerAdapter` used to live here. They moved to
[`CKli.ArtifactHandler.Plugin`](../CKli.ArtifactHandler.Plugin/README.md#nugetfeedclient--talking-to-a-feed)
on 2026-09-09, where the `NuGetFeed` they talk to is also defined: a consumer that only wants to look up
the versions of a package has no business depending on the publication plugin. This plugin obtains its
clients from `NuGetFeed.CreatePushClient( monitor, secretsStore )`, which resolves the API key and answers
null (having said why) when it cannot.

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
`PushQualityFilter`, `Credentials` naming a secret resolved via `ISecretsStore`) — see that
plugin's README for the exact shape. Git-hosting credentials (used to create releases) are
likewise resolved through `GitRepositoryKey` / `ISecretsStore`, documented in `CKli.Core`'s
`GitHosting` folder.

## Known rough edges (from the code)

- The gate's failure paths have no integration test coverage: `Tests/Plugins.Tests` never reaches
  `PublishableStatus.IndirectPublishRequired`, so the `RequiredPublications` closure, its
  producers-first ordering, `IndirectPublisher`, and `BuildFinalProfile`'s conflict branch are all
  exercised only by reasoning. Constructing that state needs a fixture with a second configured
  branch.
- `NuGetFeedClient.DeleteAsync` (now in `CKli.ArtifactHandler.Plugin`) is fully implemented but never
  invoked by `PackageSender` or any publisher — there is currently no CKli-level command that
  deletes/unlists a published package version.
- `BasePublisher`'s `$Local` cleanup used to be skipped by matching a hard-coded test path
  (`/.PublicStack/CK-Plugins/Tests/Plugins.Tests`). That literal never matched any real folder — CKli's
  own harness lives in `CKli-Plugins`, not `CK-Plugins` — so the cleanup always ran, including in tests,
  and the "trick for the tests" its comment described never happened. It is now driven by the
  `KeepLocalReleaseAfterPublish` configuration above, left off by default: `Tests/Plugins.Tests` has only
  ever been green with the cleanup on, and enabling it makes `S2.intermediate_build_error_Async`'s
  `ckli publish` fail. Whether that test or the intended behavior is wrong is still open.
