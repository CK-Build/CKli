# CKli.VersionTag.Plugin

Manages **CSemVer version tags** on a `Repo`'s commits: discovers, validates, and indexes every version tag in a
repository, computes the next version to build, tracks release dependencies across the whole World, and implements
version deprecation. It is the plugin that turns a bag of git tags into a coherent, queryable release history.

## Why

CKli builds NuGet/artifact releases by tagging commits with CSemVer versions (`v1.2.3`, `v1.2.3-alpha.1`, CI builds,
etc.), using the [CSemVer](https://csemver.org) library (`CK.Core.SVersion`, `CSVersionKind`, `SVersionChange`...).
Tags alone are a flat, unordered set with no guarantee of consistency: duplicate versions, versions on the wrong
commit, gaps in the Major.Minor.Patch sequence, orphaned CI tags, etc. `VersionTagPlugin` is the component that:

- **Parses and validates** every tag in a repo against CSemVer + CKli's own conventions (`local/`, `building/`
  prefixes; `+fake`, `+deprecated`, `+invalid` build metadata; `ci.0`/`--ci.0` markers), and turns the valid ones
  into an indexed, conflict-free view (`VersionTagInfo`).
- Computes the **"hot zone"**: the span of commits between the last published stable version and the current
  highest ("top hot") version tag reachable from any branch. This hot zone is what `CKli.BranchModel.Plugin` walks
  to compute branch synchronization, and what a build process walks to determine "what to build next".
- Decides the **next version number** for a build (`TagCommitTree.ComputeTargetVersion`), taking into account
  Conventional Commits (`feat:`, `BREAKING CHANGE`, ...), branch name (prerelease suffix), and CI commit depth.
- Maintains a cross-repository **release graph** (`ReleaseDatabase` / `RepoReleaseInfo`) built from the
  `Consumed`/`Produced` package lists embedded in each tag's annotation, so that a release's producers and consumers
  can be found without re-parsing every repo.
- Implements **version deprecation** (`+deprecated` tags with an expiration date) and propagates it transitively to
  every downstream consumer in the Stack.

### Where it sits in the plugin ecosystem

| Plugin | Relationship |
|---|---|
| `CKli.ArtifactHandler.Plugin` | Required dependency (constructor-injected). Supplies `BuildContentInfo` (the `Consumed`/`Produced`/`AssetFileNames`/`Transitive` payload written into a tag's annotation) and performs the actual artifact deletion behind `DestroyLocalRelease`. |
| `CKli.BranchModel.Plugin` | Required dependency. `VersionTagPlugin` registers itself as the World's `ITagCommitProvider` (`branchModel.SetTagCommitProvider(this)`) so that `HotBranch.Synchronize` can resolve, for `Release`/`CI`-linked branches, "what was last built on the parent branch". |
| A Build plugin (e.g. the project that drives `dotnet build`/`pack`) | Consumer. Calls `VersionTagInfo.TryGetCommitBuildInfo` to validate a candidate (commit, version) pair before building, then `CommitBuildInfo.ApplyReleaseBuildTag` to tag the result. |
| `CKli.Publish.Plugin` | Consumer of the release graph (`ReleaseDatabase`) and of published/local version state to decide what can be pushed to a feed. |
| CKli.Core's `tag list/fetch/pull/push/delete` commands | Generic git-tag transport (any tag, any ref) — VersionTag.Plugin does **not** implement these; it only interprets *version* tags once they exist locally. |

A World enables this plugin (indirectly, by referencing it from a plugin that needs versioning) whenever it produces
versioned artifacts (NuGet packages, etc.) that must be reproducibly built and released from git tags.

## How

### Base type and construction

`VersionTagPlugin` is a `sealed partial class VersionTagPlugin : PrimaryRepoPlugin<VersionTagInfo>,
BranchModel.Plugin.ITagCommitProvider`. As a `PrimaryRepoPlugin<T>` it is always instantiated, and it creates one
`VersionTagInfo` per `Repo` on demand (cached, like any `RepoPluginBase<T>`).

```csharp
public VersionTagPlugin( PrimaryPluginContext primaryContext,
                          ArtifactHandlerPlugin artifactHandler,
                          BranchModelPlugin branchModel )
```

At construction it subscribes to `World.Events.Issue` (to report per-repo version-tag issues through `ckli issue`)
and to `World.Events.CreateLTS` (to cut the version range between the new LTS World and the current one), and
registers itself as the `ITagCommitProvider` for the `BranchModelPlugin`.

### Configuration

Global, on the `<VersionTag>` element under the World's `<Plugins>`:

```xml
<VersionTag AutoFixRemovableTag="false" RemoveUselessFakeTag="false">
  <Packages>
    <Package Name="SomeExternalPackage" Version="3.2.1[LockMajor]" />
    <Package Name="AnotherExternalPackage" Version="1.0.4[Lock]" />
    <Package Name="Microsoft.AspNetCore.*" Version="8.0.0[LockMajor]" />
  </Packages>
</VersionTag>
```

| Attribute/Element | Meaning |
|---|---|
| `AutoFixRemovableTag` (bool, default `false`) | If true, tags identified as safely removable (superseded, duplicated, resolved by a `+invalid`, ...) are deleted locally as soon as they are discovered, instead of only being reported as an issue. |
| `RemoveUselessFakeTag` (bool, default `false`) | If true, a `+fake` tag whose real version has since been published is deleted locally instead of kept around. |
| `<Packages><Package Name="..." Version="..."/></Packages>` | World-wide declared **version bounds** for packages that are consumed but not produced by any repo in the Stack (external dependencies). The `Name` is an exact package identifier, or one with `*` wildcards that covers a family. Exposed via `GetPackagesConfiguration` as a `PackageBounds`. |

The two booleans are *plugin attributes*: `ckli plugin info` describes their current value and
[`ckli plugin set RemoveUselessFakeTag true`](../../CKli.Core/CKliCommands/README.md#plugin-set-name-value)
(or `ckli plugin unset RemoveUselessFakeTag`) writes them without editing this file by hand. The
`<Packages>` element has no such support: it must be edited in the World definition file.

The `Version` is a [`SVersionBound`](https://github.com/CK-Build/CK-SVersion), not a single version: it is the
range of versions this World accepts for that package. It is parsed by `SVersionBound.TryParse`, so it is a base
version optionally followed by its restrictions between brackets:

| `Version` | Accepts |
|---|---|
| `3.2.1` | Any version `>= 3.2.1`, CI versions excluded. This is a **floor**, not a pin. |
| `3.2.1[Lock]` | `3.2.1` and nothing else: a true pin. |
| `3.2.1[LockMajor]` | Any `3.x` at or above `3.2.1`. |
| `3.2.1[LockMinor,Stable]` | Any stable `3.2.x` at or above `3.2.1`. |
| `3.2.1[AllowCI]` | `>= 3.2.1`, CI versions included. |

A bound that excludes its own base version is a configuration error, and so is a duplicated `Name`.

In the `Name`, **each `*` stands for any sequence of characters**, possibly empty. That covers a family in one
line instead of one per identifier - and covers the member the family gains next week:

| `Name` | Matches |
|---|---|
| `Microsoft.AspNetCore.*` | A **prefix** family: `Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.Authentication.OpenIdConnect`... This is what a *framework coupled* family needs - those packages only ship assets for their own .NET generation, so on a `net8.0` World they must all stay on `8.x`. |
| `*.Abstractions` | A **suffix** family: `CK.Core.Abstractions`, `Microsoft.Extensions.Configuration.Abstractions`... |
| `CK.*.Engine` | A **role** family whatever sits in the middle: `CK.IO.Engine`, `CK.DB.Zone.Engine`... A `*` crosses the dots - it is any sequence of characters, not one segment. |
| `CK.Core` | Exactly that identifier, and nothing else. |

Both ends matter: `CK.*.Engine` does **not** match `CK.IO.Engine.Tests`, because the last literal is anchored at
the end of the identifier - and `Microsoft.AspNetCore.*` does not match `Microsoft.AspNetCore`, because the `.` is
part of the literal. A `*` matches an empty sequence, so `CK.*.Engine` does match `CK..Engine`.

```xml
<Packages>
  <!-- The exception comes FIRST: it is reached only if nothing above it matched. -->
  <Package Name="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="8.0.30[Lock]" />
  <!-- ...and the rest of the ASP.NET Core family stays on 8.x. -->
  <Package Name="Microsoft.AspNetCore.*" Version="8.0.0[LockMajor]" />
</Packages>
```

### The first `<Package>` that matches wins

**The declaration order is the priority order**, exactly like the routes of a web router: `deps update` walks
the `<Package>` elements from top to bottom and stops at the first `Name` that matches the identifier it is
resolving. Nothing else is taken into account - not how specific a `Name` looks, not whether it is exact or a
pattern:

| | |
|---|---|
| **Order decides, and only order** | `Microsoft.AspNetCore.Http` before `Microsoft.AspNetCore.*` is that family's exception; the *same* line written after the family would never match, because the family already answered. |
| **Coarse after fine** | `Microsoft.AspNetCore.*` then `Microsoft.*` means "the ASP.NET Core family, then everything else Microsoft". Written the other way round, `Microsoft.*` answers first and the finer line is dead. |
| **Matching is case insensitive** | Like package identifiers everywhere else. |
| **A duplicated `Name` is an error** | Not a priority: the second one could never match anything. |
| **A `Name` needs one literal character** | A name made only of `*` is refused: a bound carries a base version, so it applies to a family and not to every external package of the World. Two consecutive `*` are refused too - always a typo, since one already matches any sequence. |
| **An unreachable `<Package>` is a warning** | A rule declared after one that already covers it can never answer - a family written before its own exception is the usual way to get there. The configuration still means something coherent (the family bound applies), so the command warns and runs. |

```
<Package Name="Microsoft.AspNetCore.Http" /> can never match: <Package Name="Microsoft.AspNetCore.*" /> is
declared before it and already covers it. The first <Package> that matches wins, so an exception must be
declared before the family it excepts.
```

Order over specificity is a deliberate choice, and not NuGet's
([`packageSourceMapping`](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping) ranks
its patterns by prefix length). **It is what makes a `*` usable anywhere in a `Name`**: a specificity ranking has
no canonical answer as soon as patterns overlap in more than one way - for `X.IO.Thing` matched by both
`X.*.Thing` and `X.IO.*`, "longest literal prefix" and "most literal characters" pick *different* winners - so
ranking would have forced the wildcards to stay in the one place where length is a total order. First-match-wins
needs none of that, and a new kind of `Name` can be added later with its priority already defined by where you
write it.

A matched bound then applies **exactly as if the identifier had been named**: a rule decides what a bound
covers, never what it does. Mind that the "bring an out-of-bound reference back to the base version" half of a
bound applies family-wide too, so a pattern broader than the family it means drives real edits - where an
exact `Name` that matches nothing is simply inert. `ckli deps update --dry-run` reports which rule holds or
moves which identifier: that report is the place to check a pattern's reach before applying it.

This bound is an **invariant of the World**, and the two consumers enforce it the same way: a referenced version
that is in its bound is left alone, one that is not is brought back to the bound's base version.

- `ckli build` (through `HotGraph.PackageUpdater.WorldConfiguredMapping`) rewrites the out-of-bound references of
  the solutions it builds.
- [`ckli deps update`](../CKli.Build.Plugin/README.md#deps-update-aligning-the-external-dependencies) does the same,
  and the bound also caps what it may propose: a World Reference or a feed that offers a version outside the bound
  is refused, and with `--with-nuget` the greatest version *inside* the bound is the target (so `3.2.1[LockMajor]`
  tracks the latest `3.x` and ignores a published `4.0.0`).

Per-repo, under that repo's plugin configuration element:

| Attribute | Meaning |
|---|---|
| `InfVersion` | Exclusive lower bound: tags `<= InfVersion` are ignored. Read/written via `XNames.InfVersion`; set by [`ckli world lts create`](#worldeventscreatelts--cutting-the-version-range-of-a-new-lts) on the default World, and by `SetInfVersion` for a one-time .NET 8 migration path (which must be called before the repo's `VersionTagInfo` is created). |
| `SupVersion` | Exclusive upper bound: tags `>= SupVersion` are ignored. Only meaningful in an LTS World — in the default World it is a warning and gets stripped automatically. Set by [`ckli world lts create`](#worldeventscreatelts--cutting-the-version-range-of-a-new-lts) on the new LTS World. |

### Commands

| `[CommandPath]` | Purpose |
|---|---|
| `version bump` | Sets a `+fake` version tag on the repo's root (or `dev/`) branch to retroactively start a new Major.Minor.Patch line above every currently known version, cleaning up now-superseded local builds and higher `+fake` tags first. |
| `version deprecate` | Marks a published version as `+deprecated` (with an expiration date computed from `--immediate`/`--days`), and recursively propagates the deprecation to every repo/version in the Stack that (transitively) consumes it. Never the *alive* version of a repository (see below). |

Both commands take `IActivityMonitor` and `CKliEnv` first, matching the plugin command convention. Selected
parameters:

- `version bump <version>` — `version` must be a stable `Major.Minor.Patch` (no prerelease/build metadata), greater
  than every existing non-fake, non-local version and within `[InfVersion, SupVersion[`.
- `version deprecate <version> [--reason <text>] [--days <n> | --immediate] [--allow-update]` — `--days`/`--immediate`
  are mutually exclusive when creating; `--allow-update` is required to edit an existing, non-expired `+deprecated`
  tag. The version must not be the repository's **alive** version (`VersionTagInfo.AliveStable`: the top stable that
  was actually published — `HotZone.LastStable`, or the published stable below it when that one is, or carries, a
  `+fake`). Deprecating it would leave the repository with nothing usable at all: publish a replacement first, and the
  previous version is then no longer alive. This guards the *creation* of a deprecation only — a deprecation is
  irreversible, only its expiration can change, so `--allow-update` still works on an alive version that already
  carries a `+deprecated` tag (a state only a manual tag edit can produce).

Note: the day-to-day raw tag transport commands (`tag list`, `tag fetch`, `tag pull`, `tag push`, `tag delete`) live
in **CKli.Core**, not in this plugin — VersionTag.Plugin only reasons about tags once they're present locally.

### `World.Events` subscriptions

`World.Events.Issue` (`IssueRequested`): for every requested repo it calls `Get(monitor, repo)` (building/caching
the `VersionTagInfo`) and lets it `CollectIssues` into the issue screen.

`World.Events.CreateLTS` (`LTSCreated`), described next.

### `World.Events.CreateLTS` — cutting the version range of a new LTS

`ckli world lts create <@ltsName>` clones the default World's definition; this plugin's `Sync` handler
(`VersionTagPlugin.LongTermSupport.cs`) splits the version range of every repository between the two Worlds and
strips the new World's branch model. `ComputeRepoLTSVersions` computes the plan and validates it; `LTSCreated`
only applies it, so a refusal writes nothing at all.

Per repository the **cut** is `Major + 1` of the version it currently offers (`HotZone.LastStable`), so a `v1.2.4`
cuts at `2.0.0-0` and a `v0.4.1` at `1.0.0-0` — the 0.x convention that treats the Minor as the breaking axis is
deliberately *not* applied, unlike `SVersion.SetNextVersionNumbers( SVersionChange.Major )`. It becomes:

- the new LTS World's `SupVersion`, so that World keeps every version produced so far (and keeps its cloned
  `InfVersion`);
- the default World's `InfVersion` (`RepoLTSVersion.NextInfVersion`), so that World starts a new line above the cut.

**The cut is written as its `-0` prerelease** (`2.0.0-0`, not `2.0.0`). Both bounds exclude themselves, so a cut
that a repository could actually produce would belong to *no* World at all. `-0` is the smallest possible version
of its `Major.Minor.Patch` and is not a conformant CSemVer version, so nothing can ever land on it — and
`BuildPlugin`'s "Missing initial version" fix, which builds its `+fake` tag from `InfVersion`'s
`Major.Minor.Patch` alone, then starts the default World exactly *at* the cut (`v2.0.0+fake` → `v2.0.0`). This is
the same projection `MigrationPlugin.InitializeInfVersionFromMaster` applies.

`ComputeRepoLTSVersions` refuses the creation when:

- any repository has a version issue, or any has a branch issue (`TryGetAllWithoutIssue` on both plugins);
- the version a repository currently offers is `building/`/`local/`, `+fake` or `+deprecated` — there would be no
  published version to cut at;
- a repository has a **pending `local/`/`building/` release** (`VersionTagInfo.GetLocalReleases`), even though the
  version it offers is published;
- any repository's `dev/` root branch is ahead of its root branch. Code sitting there has no version, so it would
  be inherited by the LTS World and have to be re-produced above the cut by the default World;
- any repository's root branch differs from its remote one, once fetched. Everything above is decided on the local
  repositories: a root branch behind its remote misses another developer's publication, which would end up below
  the cut and on no LTS branch. This runs under the `publish` lock that the command holds.

Every offending repository is listed, not just the first, and nothing is written on refusal — `ComputeRepoLTSVersions`
decides, `LTSCreated` only applies.

`LTSCreated` also registers a creation step (`CreateLTSEventArgs.AddCreationStep`, run once every handler has
accepted) that creates the **LTS root branch** (`@ltsName/stable`) of every repository on
`RepoLTSVersion.LTSRootCommit` — the commit of `HotZone.LastStable`, the last published version — and pushes it.
The LTS starts with its last published version, and what may follow it on the root branch stays in the default
World. The branch is created in the default World's repositories (they share their remotes with the LTS World's
ones) and deleted once pushed: the LTS World's clones obtain it from the remote. It has to be on the remotes
before the command ends, otherwise a `ckli world lts clone` run later would fix the missing root branch from what
the default root has become since. A remote branch already on the right commit is accepted (a previous attempt
pushed it), one elsewhere is an error.

The checks are independent, so several can hold at once. Each one details the repositories it concerns and
contributes a short reason; the last error then **names the actual cause(s)** —
`Unable to create a Long Term Support world: no published version at all and pending local releases.` — instead of
a catch-all. That is why the message belongs to `ComputeRepoLTSVersions` and not to its caller: it is the only one
that knows which check failed. The two `TryGetAllWithoutIssue` refusals log nothing extra, since their own message
already names the repository *and* the operation it blocks.

The pending-release check is the one none of the others can make: `HotZone.LastStable` does not see a `local/`
release of a repository that *has* a published version (a `local/` `TagCommit` heads `LastStables` only when it
carries a `FakeVersion`), and the `dev/` check does not either, because a non-CI build integrates and deletes the
`dev/` branch as it goes. Left alone, such a version is *below* the cut, so it would land in the new LTS World
while the code it was built from continues in the default one — the developer has no way to tell where his pending
work went. `GetLocalReleases` is the probe (`RemovableTags` plus `AllVersions` filtered on `IsBuildingOrLocal`),
shared with `DestroyLocalReleases`; the remedy is to publish the release or let a new build supersede it.

The `dev/` check is a **tip-SHA comparison**, which is worth knowing when writing a test: a World arranged by the
fake build harness never satisfies it, because the harness leaves the repositories' content — version tag included
— on `dev/stable` with `stable` behind. An `LTSCreateTests` arrange therefore has to publish first.

The new World's `<BranchModel>` keeps only its root branch (`BranchNamespace.CreateForLTS`), written by a single
`WriteConfiguration()` call: it sets `Root` and replaces the cloned `<Prerelease>` and `<Explo>` elements (both
empty here), leaving `AutoFixUselessBranch` alone. Both halves are
in the **configuration** form — without the `{LTSName}/` prefix — because `BranchNamespace` prepends that prefix
itself when it reads back, and the `Root` parser rejects a name that does not match `^[a-z][0-9a-z_-]+`: an
`@net8/stable` value makes the new World unloadable (`Invalid Root branch name in BranchModel
configuration`), and a leftover `<Explo>` is the same failure from the other side (its `Parent` no longer
resolves). That form is `BranchName.ConfigurationName`, and it is what every BranchModel config writer uses —
see `BranchModel/README.md`.

The event args expose `GetLTSRepositoryElement( Repo )` to reach a repository's `<Repository>` element in the
clone. Use it rather than matching on the `Url` attribute: that attribute holds a *Repository Proxy* name instead
of an url whenever `StackRepository.LocalProxyRepositoriesPath` applies (which is the case for every test fixture).

### `VersionDeprecated` — the extension point this plugin offers

```csharp
public PerfectEvent<VersionDeprecatedEventArgs> VersionDeprecated => _versionDeprecated.PerfectEvent;
```

Raised at the very end of `version deprecate`, once every `+deprecated` tag it implies has been created or updated
**and pushed**: the deprecation is public when a handler sees it.

Each listener picks its own handler kind — `Sync`, `Async` or `ParallelAsync` — and that choice is the listener's
alone. The only constraint a `PerfectEvent` puts on the *sender* is that raising it is awaited, which is why
`DeprecateVersion` is a `Task<bool>` command (`MethodAsyncReturn.Task` in the generated dispatch). The plain
synchronous `event Action<T>` of `World.Events.Issue` / `BranchModelPlugin.ContentIssue` would have kept the command
synchronous at the cost of forcing every listener to be synchronous too.

| Member of `VersionDeprecatedEventArgs` | Meaning |
|---|---|
| `Origin` | The `RepoReleaseInfo` the command named: the root of the deprecation, and the first of `Releases`. |
| `DeprecatedInfo` | The root's `DeprecatedTagInfo`: its `Reason` and the `Expiration` at which the packages must leave the feeds. |
| `HasExpired` | Whether that expiration has passed — the version tags are gone and the packages must leave the feeds. It governs the whole propagation: a reached consumer is tagged with the origin's expiration and an already deprecated one keeps only the earlier of the two, so an expired origin means every release here has expired. `--immediate` expires it at once. |
| `Releases` | Every release that is now deprecated — the origin plus the consumers the propagation reached and could tag. |
| `DeprecatedPackages` | Those releases flattened to `PackageInstance`s: each produced package identifier at the version its release published. |

`Releases` is deliberately **not** the propagation's `visited` set. `EnsureImpliedDeprecatedTag` adds a release to
`visited` even when it stops there — no version tag left, or a `+fake` one — and only warns; such a release carries no
`+deprecated` tag and must not be mirrored as deprecated. The two collections are built side by side for that reason.

A handler that throws fails the command: `SafeRaiseAsync` logs the exception and answers false. That is the honest
outcome — the tags are pushed and cannot be taken back, but whatever mirrors them is stale, and re-running
`version deprecate --allow-update` is harmless. Its current consumer is
[`CKli.Publish.Plugin`](../CKli.Publish.Plugin/README.md), which takes the `Sync` slot and deprecates the published
profiles that carry any of the `DeprecatedPackages`.

### Version tag vocabulary

A version tag's `SVersion.ParsedPrefix` and build metadata drive how it's interpreted:

| Marker | Meaning |
|---|---|
| *(none)* | A **regular**, published version. |
| `local/v...` | A version built but not yet pushed — purely local ("rolling" build; replaced by the next local build on the same branch). |
| `building/v...` | Same idea as `local/`, used while a build is in progress (both are `IsBuildingOrLocal`). |
| `+fake` | Manually placed to bridge a gap the normal rules would reject (e.g. starting `v2.0.0` without a `v1.x.y` history), always stable, always considered published. Created by `version bump`. |
| `+deprecated` | A published version that must no longer be reproduced/consumed; carries an expiration date and the original `BuildContentInfo`. Created/updated by `version deprecate`. |
| `+invalid` | A "poison pill" that cancels a specific bad version everywhere it appears in the Stack; purely a coordination artifact, safe to remove once the bad tag is gone everywhere. |
| `ci.0` / `--ci.0` (`CINumber == 0`) | A CI-numbered mirror of a non-CI tag on the *same* commit (same-commit dual versioning for "rank 0" repos with no upstream dependencies). |

### Key types

| Type | Role |
|---|---|
| `VersionTagPlugin` | The plugin itself: commands, `ITagCommitProvider` implementation, per-repo `VersionTagInfo` factory (`Create`), release/deprecation orchestration. |
| `VersionTagInfo : RepoInfo` | Per-repo state: `InfVersion`/`SupVersion`, the version→`TagCommit` index, `HotZone`, issue lists, and operations (`TryGetCommitBuildInfo`, `DestroyLocalRelease(s)`, `AddReleaseBuildTag`, `RemoveTagCommit`). |
| `VersionTagInfo.HotZoneInfo` | The `LastStable`/`TopHot` pair plus a `TagCommitTree` builder/cache (`GetTagCommitTree`) that walks commit ancestry from any tip down to `LastStable`. |
| `TagCommit : ITagCommit` | One valid version tag bound to its commit; exposes `IsFakeVersion`/`IsDeprecatedVersion`/`IsRegularVersion`/`IsBuildingOrLocal`, an optional paired `CI0Version`/`CI0VersionTag`, an optional paired `FakeVersion`, and `CanBearVersion` (guards against 2 incompatible versions on one commit). |
| `TagCommitTree` | A breadth-first slice of commit history between a branch tip and `HotZoneInfo.LastStable`; computes `SVersionChange` (via prior tags + Conventional Commits parsing) and the actual next `SVersion` (`ComputeTargetVersion`). |
| `CommitBuildInfo` | Result of a validated "can I build this (commit, version)" check; exposes `InformationalVersion`, `ReleaseConfiguration`, and `ApplyReleaseBuildTag` to actually write the tag. |
| `BuildContentInfo` *(from CKli.ArtifactHandler.Plugin)* | The parsed content of a version tag's annotation: `Consumed` (`PackageInstance` list) / `Produced` (package ids) / `AssetFileNames` / `Transitive` (NuGet's resolved transitive packages, absent from an annotation written before they were recorded — see `HasTransitive`). Backbone of the release graph. |
| `DeprecatedTagInfo` | Parsed content of a `+deprecated` tag's annotation: `Reason`, `Expiration`, `DaysDelay`, `HasExpired`, plus the original `ContentInfo`. |
| `VersionTagPlugin.ReleaseDatabase` | World-wide, lazily-built graph of `RepoReleaseInfo` nodes, indexed by produced `PackageInstance`; supports `GetDirectConsumers`/`GetAllConsumers` traversal used by `version deprecate`. |
| `RepoReleaseInfo` / `RepoKey` | A (repo, version) release node with its direct/all producers and consumers, and `HasAllLocalArtifacts`. |
| `TagConflict` (internal enum) | Classifies why two tags collided: `DuplicateInvalidTag`, `InvalidTagOnWrongCommit`, `SameVersionOnDifferentCommit`, `CI0VersionOnOtherCommit`, `DuplicatedVersionTag`. |
| `VersionTagInfo.RebuildMode` (`[Flags]`) | `None` / `AllowRebuildCommit` / `AllowRebuildVersion` / `CheckPreviousVersion` and their combinations — governs how strict `TryGetCommitBuildInfo` is about reusing a commit or a version. |
| `VersionTagInfo.RemovableVersionTagIssue : World.Issue` | The auto-fixable issue that deletes a batch of locally removable tags when executed via `ckli issue --fix`. |
| `XNames` | Shared `XName`s for the plugin's XML configuration (`InfVersion`, `SupVersion`, `Version`, `AutoFixRemovableTag`, `RemoveUselessFakeTag`). |

### `VersionTagInfo.Create`: building the per-repo tag index

`VersionTagPlugin.Create(monitor, repo)` is where a repo's raw git tags become a `VersionTagInfo`. It is a
multi-pass algorithm (see `VersionTagPlugin.cs`):

1. **First pass** (`FirstTagCollect`) walks every git tag, keeps only tags that are `GitRepository.IsCKliValidTagName`
   and parse as a conformant `SVersion` inside `]InfVersion, SupVersion[`, and buckets them: `+invalid` markers,
   `+deprecated` tags (parsed via `DeprecatedTagInfo.TryParse`, or flagged bad if unreadable), CI `.0` markers, and
   otherwise "regular" tags (parsed via `BuildContentInfo.TryParse`, or flagged as
   `lightweightOrUnreadableRegularTags` if the annotation can't be read — these become an auto-fixable "rebuild"
   issue reported by the Build plugin).
2. `+invalid` tags are applied to cancel out any matching bad/unreadable tag collected so far.
3. **Second pass** builds the `SVersion -> TagCommit` index (`v2c`), resolving collisions: `+fake` vs. regular,
   `+deprecated` vs. regular, two regular tags on the same commit (best one wins by annotation quality then `v`
   prefix), or a genuine conflict (recorded in `tagConflicts`). While doing so it tracks `topHot` (greatest version)
   and `lastStable` (greatest published, non-local stable version — or a `+fake`/its associated `local/` peer).
4. `ci.0` tags are then matched to their base (same commit, `CINumber` cleared) `TagCommit` or turned into a conflict
   / removable tag.
5. If `AutoFixRemovableTag` is set (and no blocking conflicts/issues exist), removable tags are deleted locally right
   away; otherwise everything is surfaced through `CollectIssues` for `ckli issue`.
6. `HotZoneInfo.Create` flags a "hot zone issue" when `topHot` is inconsistent with `lastStable` (e.g.
   `topHot >= (lastStable.Major+1).0.0` with no `+fake` covering the gap) — this must be fixed manually.

### Computing the next version

`TagCommitTree.ComputeTargetVersion` (called by a Build plugin) starts from `HotZone.LastStable`, bumps it by the
requested/inferred `SVersionChange` (`GetVersionChange`, driven first by any intermediate tag's own delta, then by
Conventional Commit headers / `BREAKING CHANGE` in the commit range up to the hot zone), applies the branch name as
a prerelease suffix with an auto-incremented prerelease number, and finally sets the CI depth
(`GitRepository.ComputeCommitDepth`) when a CI build is requested.

`VersionTagInfo.TryGetCommitBuildInfo` is the gate a Build plugin calls before producing that version: it checks
`InfVersion`/`SupVersion` bounds, that the target commit/version pair doesn't collide with an existing one (unless
`RebuildMode` explicitly allows it), that no tag conflicts exist, and (via `FindBaseCommitByVersion`) that there is
no gap in the Major/Minor/Patch sequence relative to `LastStables` — unless bridged by a `+fake`.

### Release graph and deprecation

`ReleaseDatabase` indexes every produced `PackageInstance` (from each `TagCommit.BuildContentInfo.Produced`) to its
producing `RepoKey`, then lazily builds `RepoReleaseInfo` nodes with direct/transitive producer and consumer sets by
walking `Consumed` package lists. `version deprecate` uses this graph (`GetDirectConsumers`) to walk outward from the
deprecated version and create/refresh a `+deprecated` tag (with the earliest expiration) on every downstream
consumer, pushing tag creations (and removals, once expired) to each repo's remote via `DeferredPushRefSpecs`.

The propagation obeys the same alive-version rule as its root: a consumer whose **alive** version still consumes the
deprecated one is left alone, with a warning, and the walk stops there and on that repository's own consumers — the
same warn-and-stop treatment as a consumer whose version tag is missing or is a `+fake`. That repository must publish
a replacement first; the deprecation can then be replayed.
Once that is done it raises [`VersionDeprecated`](#versiondeprecated--the-extension-point-this-plugin-offers) with the
releases it actually tagged, so the deprecation can be mirrored outside the tags.

## Notable design notes from the code

- Comparisons and ordering intentionally reverse `SVersion.CompareTo` (`TagCommit.CompareTo`) so that
  `VersionTagInfo.LastStables` lists the newest version first.
- `SetInfVersion` exists only to support a one-time .NET 8 migration (replacing a legacy `MinVersion` attribute) and
  is explicitly documented as removable "one day" — which requires changing the `S0` scenario test, whose
  `ckli maintenance migrate net8` step is what drives `MigrationPlugin.InitializeInfVersionFromMaster` into it.
  That is also why `DoSetInfVersion` clears a legacy `MinVersion` attribute alongside writing `InfVersion`.
  `SetInfVersion`'s `!HasRepoInfoBeenCreated` guard is what makes it unusable from the `CreateLTS` handler (which
  must read every `VersionTagInfo` before it can compute a cut): the two share the private `DoSetInfVersion`
  instead, and the LTS path accepts the now-stale `VersionTagInfo` because the World is closed immediately after
  the event.
- The hot-zone commit walk deliberately avoids LibGit2Sharp's `CommitFilter`/`QueryBy` and walks `Commit.Parents`
  manually, because git's TREESAME pruning can skip parents when empty commits are involved (see comments in
  `HotZoneInfo.CreateTagCommitTree`).
