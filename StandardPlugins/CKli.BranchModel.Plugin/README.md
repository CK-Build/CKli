# CKli.BranchModel.Plugin

**CKli.BranchModel.Plugin** defines and enforces CKli's branching convention: the ordered set of
"hot" branches (`stable` plus opened CSemVer pre-release branches, and ad-hoc `explo/` branches),
each with an optional `dev/` working branch, kept synchronized according to a configurable
propagation rule. It is the plugin every other build/version-related plugin in the `StandardPlugins`
tree is built on top of.

## Why

CKli's overall build model (see [`CKli.Build.Plugin`'s README](../CKli.Build.Plugin/README.md))
relies on a well-known branch topology being consistent across *every* repository of a World: the
same branches must exist (or not) everywhere, a build on one repository's `dev/xxx` must be able
to reliably reach a consumer's `dev/xxx`, and "what does this branch mean, version-wise" must be
answerable without inspecting repository-specific conventions.

**CKli.BranchModel.Plugin** is what makes this topology a first-class, shared, mutable-but-audited
concept instead of an implicit assumption:

- It parses and persists the World's **branch namespace** (`<Plugins><BranchModel>` configuration)
  — the ordered list of opened branches and how each one is linked to its parent.
- For every repository it resolves this abstract namespace against the actual Git branches
  (`HotBranch`), tracking each branch's optional `dev/` branch and any desynchronization between
  the two.
- It exposes the `branch open`, `branch close`, `branch switch`, `branch sync` and `commit`
  commands used to open/retire pre-release lines, move the working folder around, and re-propagate
  changes.
- It detects and (mostly) auto-fixes branch-related problems through the `ckli issue` machinery
  (missing root branch, orphan `dev/` branches, desynchronized/unrelated branches...), and exposes
  a `ContentIssue` event other plugins use to piggy-back their own per-branch content checks
  (missing files, `.slnx` solution issues, casing problems...) onto the same "detect → fix →
  commit" flow.

Almost every other plugin depends on it, directly or transitively: `CKli.VersionTag.Plugin` (which
in turn feeds `CKli.HotZone.Plugin`, `CKli.Build.Plugin`, `CKli.Publish.Plugin`), plus
`CKli.CommonFiles.Plugin`, `CKli.ArtifactHandler.Plugin` and `CKli.Migration.Plugin`. Its own
`.csproj` only references `CKli.ShallowSolution.Plugin` (needed for the `ContentIssue` event's
access to a branch's `.slnx`/file content).

`VersionTagPlugin` completes the loop the other way: it implements `ITagCommitProvider` and calls
`BranchModelPlugin.SetTagCommitProvider(this)` in its constructor, so that `BranchLinkType.Regular`
/ `BranchLinkType.CI` synchronization (see below) can find "the last built commit of the parent
branch" without `BranchModelPlugin` depending on the versioning plugin.

## How

### Plugin wiring

`BranchModelPlugin : PrimaryRepoPlugin<BranchModelInfo>` (see
[CKli.Core's README](../../CKli.Core/README.md) for the plugin base classes) — it is always
instantiated, and caches one `BranchModelInfo` per `Repo`. Its constructor reads the
`<BranchModel>` element from `PrimaryPluginContext.Configuration`, builds the `BranchNamespace`,
and subscribes to `World.Events.Issue`.

```csharp
public sealed partial class BranchModelPlugin : PrimaryRepoPlugin<BranchModelInfo>
{
    public BranchModelPlugin( PrimaryPluginContext primaryContext, ShallowSolutionPlugin shallowSolution );

    public BranchNamespace BranchNamespace { get; }
    public event Action<ContentIssueEventArgs>? ContentIssue;
    public void SetTagCommitProvider( ITagCommitProvider commitProvider );
}
```

### The branch namespace: `BranchName` / `BranchNamespace`

A **`BranchName`** is an immutable node in a tree: it has a `Name`, a `DevName` ("`dev/`" +
`Name` in the default World, "`@lts/dev/`" + the rest of the name in a Long Term Support one, so that every git
branch of a LTS World is under its "`@lts/`" prefix), a `ConfigurationName`, an `Index` (used to align with per-repo arrays), a `Parent` (null
only for the root), a `LinkType` describing how it is kept in sync with its parent, and a
`VersionKind` (`CSVersionKind`, from CSemVer — `Stable`, `Alpha`..`Zulu`, or `Exploratory`).

**`Name` is the actual git branch name; `ConfigurationName` is the same name without the
`{LTSName}/` prefix, and it is the only one that may be written to the configuration.** They differ
only in an LTS world, which is what makes getting it wrong easy and expensive: the `BranchNamespace`
constructor *prepends* the prefix as it reads, and its `Root` parser requires
`^[a-z][0-9a-z_-]+`, so a prefixed `@net8/stable` written back is not merely re-prefixed — it is
refused, and the world can no longer be loaded at all.

A **`BranchNamespace`** owns the whole tree for a World:

- **`Root`** — the single stable branch (`stable` by default, configurable, prefixed with the
  World's `WorldName.LTSName` in an LTS world, e.g. `net8/stable`).
- **`MainLineBranches`** — the root followed by the opened CSemVer pre-release branches
  (`alpha`..`zulu`), ordered from least to most stable, each with its own `LinkType`. This is what
  the `Root` XML attribute and the `<Prerelease>` elements encode.
- **`ExploratoryBranches`** — `explo/name` branches, each anchored under a main-line branch or
  under another `explo/` branch (arbitrary nesting), configured as `<Explo>` child elements.

`BranchNamespace` is immutable: `AddOrUpdate`, `AddOrUpdateExplo` and `Remove`
(`BranchNamespace.Mutators.cs`) all return a *new* namespace (plus the affected `BranchName`)
rather than mutating in place. `BranchModelPlugin` persists the result back to the World's XML via
`SaveBranchNamespace` after any command that mutates it.

#### Configuration XML

```xml
<Plugins>
  <BranchModel Root="stable" AutoFixDevBranch="true">
    <Prerelease Name="beta" Link="Regular" />
    <Prerelease Name="rc" Link="CI" />
    <Explo Name="explo/spike-x" Parent="beta" Link="Manual" />
    <Explo Name="explo/spike-x-sub" Link="CI" />  <!-- nested under explo/spike-x -->
  </BranchModel>
</Plugins>
```

- **`Root`** (attribute, optional) — the root branch name. Defaults to `"stable"` when absent. It is a
  lowercase ASCII identifier (dash and underscore allowed) and cannot be a prerelease name nor `explo`.
- **`<Prerelease Name="..." Link="...">`** — an opened prerelease branch. `Name` is a conformant
  SVersion prerelease name (`alpha`, `bravo`, ... `zulu`). **Their order in the document is
  irrelevant**: those names carry a total order, so the parent chain is a function of the names alone
  and the parser sorts them (`AddOrUpdate` sorts the same way when it adds one). Only a *duplicate*
  `Name` is an error — that is one branch with two link types. They are written back in decreasing
  CSemVer stability order, which is the parent chain read top down. `Link` defaults to `Full`.
  This is what separates them from `<Explo>`: an exploratory name carries no order, which is why that
  element needs an explicit `Parent` (or XML nesting) and this one does not.
- **`AutoFixDevBranch`** (attribute, optional, default `true`) — keeps each `dev/` branch consistent
  with its base, silently, instead of reporting an issue: a `dev/` that has nothing ahead of its base
  (a "useless" `dev/`) is deleted, and the missing base of a `dev/` branch is recreated where the `dev/`
  branch left its closest existing parent (`HotBranch.RestoreMissingBase`). A `dev/` branch implies its
  base, but a base can be missing: a `dev/` branch fetched from a remote that doesn't have its base, or a
  base deleted locally. Recreating it loses nothing: the `dev/` branch keeps its commits, ahead of its
  base. When it is false, `ckli issue --fix` does the same. This is a *plugin attribute*: `ckli plugin
  info` describes its current value and
  [`ckli plugin set AutoFixDevBranch false`](../../CKli.Core/CKliCommands/README.md#plugin-set-name-value)
  (or `ckli plugin unset AutoFixDevBranch`) writes it without editing this file by hand.
- **`<Explo Name="..." Parent="..." Link="...">`** — an exploratory branch. `Name` must be
  `explo/<lowercase-id>` (the `explo/` prefix and the LTS prefix are inferred if omitted) and must
  neither start with `ci-` nor end with `-ci` — that suffix qualifies a branch name to name its CI
  builds apart from its regular versions (the Publish plugin's `Published/index.json` does exactly
  that), so `explo/spike-ci` and the CI line of `explo/spike` would be one name. An exploratory name
  is the only branch name that is free: `alpha` to `zulu` are fixed and by design none of them
  collides. The rule is `SVersion.IsReservedExploratoryName` — `SetExploratoryName` and the version
  parser apply it on the version side, and this plugin applies it at the three sites that accept an
  exploratory name: here, `branch open` and `AddOrUpdateExplo`. `Parent`
  is required only on a *root* `<Explo>` (nested `<Explo>` elements inherit their XML parent).
  `Link` defaults to `Full`.

**`Link` is spelled by its name — `Manual`, `Regular`, `CI` or `Full` — everywhere**: on a
`<Prerelease>`, on an `<Explo>`, and as the `--link` option of `ckli branch open`. It is *always
written back*, including the `Full` default: a World definition file states what is true instead of
relying on a default its reader has to know. Reading stays tolerant, so a hand-written element that
omits it is `Full`.
`BranchLinkType.None` ("not specified") is the root branch's link type and nothing else: `AddOrUpdate`
and `AddOrUpdateExplo` resolve it to the branch's current link type or to `Full` for a new branch, and
`Rebuild` refuses it outright.

The compact codes — `|✋` (Manual), `|>` (Regular), `->` (CI), `=>` (Full) — are **display only**
(`BranchLinkTypeExtensions.ToCodeString`). They are never stored and never parsed: `ckli branch list`
and `ToParentedString()` are their only consumers. Three of the four contain a `>` that XML would
escape, and the fourth is an emoji, which is why the configuration does not use them.

`WriteConfiguration(XElement)` serializes back into this same shape — it sets `Root` and replaces the
`<Prerelease>` and `<Explo>` elements, leaving any other attribute (`AutoFixDevBranch`) untouched
— using `BranchName.ConfigurationName` so the round trip holds in an LTS world too
(`BranchNamespaceTests.lts_namespace_configuration_round_trips` pins it). `ToConfiguration()` is the
same thing into a fresh element, and `ToString()` is that element. `GetDisplayTree()` renders the whole
tree indented for *display* and keeps the real branch names — the two must not be confused: anything
that ends up in a `<BranchModel>` element goes through the former.

Reading is tolerant of both forms — an `<Explo>` `Name` or `Parent` may carry the `{LTSName}/` prefix
or not — so an LTS world's hand-written configuration keeps working either way.

`BranchNamespace.CreateForLTS( ltsName )` returns a root-only namespace under a new LTS name. It is
what [`ckli world lts create`](../CKli.VersionTag.Plugin/README.md#worldeventscreatelts--cutting-the-version-range-of-a-new-lts)
writes into the new World: an LTS starts with no pre-release and no exploratory branch open.

#### Link types: how a `dev/` child is kept in sync with its parent

| `BranchLinkType` | Code | Meaning |
|---|---|---|
| `None` | *(root only)* | Not applicable — the root branch has no parent. |
| `Manual` | `\|✋` | No propagation at all; the `dev/` branch must be updated by hand. |
| `Regular` | `\|>` | The parent's **regular (stable, pre-release or exploratory) version commits** are merged in — a build must have produced a *regular version* on the parent to reach this branch. |
| `CI` | `->` | The parent's **built commits** (including CI builds) are merged in — any build (`build` or `build --regular`) on the parent reaches this branch. |
| `Full` | `=>` | *(default)* The parent's **`dev/` tip** is merged in directly, regardless of whether it was ever built. |

`Regular`/`CI` synchronization needs to know "what was last built on the parent" — this is exactly
what the injected `ITagCommitProvider` (`VersionTagPlugin`) answers via `GetCommit(monitor, branch,
allowCI)`.

### Per-repository resolution: `BranchModelInfo` / `HotBranch` / `BranchLink`

`BranchModelInfo : RepoInfo` is the per-`Repo` cache entry (`PrimaryRepoPlugin<T>.Get`/`Create`).
For each `BranchName` in the namespace it builds one **`HotBranch`**, indexed the same way
(`Branches[name.Index]`):

- `HotBranch.GitBranch` — the actual `Branch` if it exists in the repository (`Exists` is false
  otherwise; only the root branch missing is a blocking issue).
- `HotBranch.GitDevBranch` — the corresponding `dev/` branch if it exists.
- `HotBranch.Parent` — the parent `HotBranch` (mirrors `BranchName.Parent`).

The relationship between a `GitBranch` and its `GitDevBranch` is captured by an immutable
**`BranchLink`** (`Branch` = base, `Ahead` = the `dev/` branch), which classifies the pair into an
`IssueKind`:

| `IssueKind` | Meaning |
|---|---|
| `None` | Fine: `Ahead` doesn't exist, or is strictly ahead of `Branch`. |
| `Useless` | `Ahead` has no commit beyond `Branch` — should be deleted (auto-fixed unless checked out and dirty, see `AutoFixDevBranch`). This is about commits, not content: an `Ahead` with commits of its own and the same tree as `Branch` (a "Producing 'vX' from unchanged head." commit that carries a version) is not useless. |
| `Unrelated` | `Ahead` shares no common ancestor with `Branch` — must be fixed manually. |
| `Desynchronized` | `Ahead` is behind `Branch` — fixable by merging `Branch` into `Ahead`. |
| `DesynchronizedCheckout` | Same as above, but `Ahead` is checked out and the working folder is dirty — cannot be auto-merged. |

`HotBranch` exposes the operations that keep this model consistent:

- **`EnsureExists`** — creates the branch from the closest existing ancestor if missing.
- **`Close`** — integrates `dev/` into the branch, then merges the branch into the `dev/` branch of its
  closest existing parent (created if needed) and deletes it (used to retire a pre-release line). The
  parent's base branch is not touched: a base branch only moves when its `dev/` branch is integrated. The
  merge is the one of `Synchronize` (package versions aligned with a resolver, left in progress on a
  conflict when asked).
- **`PredictSynchronize`** / **`PredictClose`** — the dry runs of `Synchronize` and `Close`: the merge is computed
  in the object database only and its `MergeOutcome` is returned (`UpToDate`, `FastForward`, `Merge`, `Conflict`
  with the `PreparedMerge` that would be left in progress, or `Failed`). `PredictSynchronize` starts from the local
  branches: the merges of the remote branches depend on a fetch and are not predicted.
- **`PreparedMerge`** — a merge that `Synchronize` or `Close` left in progress in the working folder (their
  `onPreparedMerge` callback receives it): the repository, the checked out branch, what is merged and the
  paths in conflict. `PreparedMerge.Display` is how the commands show them.
- **`OpenOrPredict`** / **`SynchronizeOrPredict`** / **`CloseOrPredict`** — what the `branch open` (and
  `branch switch --create`), `branch sync` and `branch close` commands run in each repository: the operation, or its prediction in a dry run. They all report to
  a **`BranchMergeReport`**, created once per command: it carries the dry run flag and the `onPreparedMerge`
  callback (only when a version resolver is available: without it, a conflict simply fails), collects the predicted
  outcomes and the prepared merges, and displays them at the end of the command. `OpenOrPredict` creates a missing
  branch with `EnsureExists`, ensures its `dev/` branch, synchronizes it and checks the `dev/` branch out; in a dry
  run, a missing branch is counted as a creation (`Dry run: 1 creation. Nothing has been changed.`). The root branch,
  that always exists, is only synchronized with its remote.
- **`CheckNoIssue`** — the issue check of the branch commands that change branches: only the branch and its closest
  existing parent (the one it is created from, synchronized with and closed into; the root has none) matter.
- **`Synchronize(monitor, applyLink, versionResolver)`** — the core propagation logic: merges tracked
  (`origin/...`) branches first, resolves `Desynchronized`/`Useless` locally, then — unless the link is
  `Manual` or `None` — synchronizes the closest existing parent with its own remote branches (as a
  `Manual` link: the parent's own link is not followed) and merges its `GetLinkCommit` into `dev/` when
  this branch doesn't already contain it. The optional `versionResolver` provides an
  `IPackageVersionResolver`: when the merge needs a merge commit, it goes through
  `ShallowSolution`'s `PackageVersionMerge`, which aligns the package versions that conflict and fails
  only on what still conflicts. It is called only when such a merge conflicts on project files (so a
  `HotGraph` is computed only then); without it, any conflict fails.
- **`GetLinkCommit(monitor, parent, linkType)`** — what a link propagates, read without changing
  anything: `Full` gives the parent's `dev/` tip (its regular tip when there is no `dev/`), `Regular`/`CI`
  the commit from `ITagCommitProvider`. `Synchronize` merges it and `GetStartCommit` creates a missing
  branch on it.
- **`Commit`** / **`IntegrateDevBranch`** / **`EnsureDevBranch`** — everyday `dev/` branch
  operations (development always happens on `dev/`; only integration touches the base branch).

`BranchModelInfo.GetClosestExistingBranch` / `GetRequiredClosestExistingBranch` walk up `Parent`
links to find the nearest branch that actually exists in Git — used whenever an operation needs
"the branch to act on" but the exact target hasn't been created yet. **The walk starts at the
given name**, so it answers that name itself when its branch exists: an operation that acts on an
*existing* branch must pass its `Parent`. `GetStartCommit` passes the name (it runs only when the
branch doesn't exist), `Synchronize` and `Close` pass the parent. `GetRequiredClosestExistingBranch` fails when a
branch that the walk skips has an orphan `dev/` branch (whatever its depth): that `dev/` branch carries work the
closest existing branch doesn't have. `AutoFixDevBranch` usually recreates the missing base first, so this happens
when it is false or when the `dev/` branch has no common ancestor with its parent.

**A missing root branch blocks the branch commands.** It is the ultimate branch issue: the `Branches` of such a
`BranchModelInfo` only contain the `Root`. `BranchModelInfo.CheckRoot` reports it (`Missing root 'stable' branch in
'X'. Use 'ckli issue --fix' to fix this.`) and `BranchModelPlugin.GetInfos`, through which every branch command
reads its repositories, fails the command before anything is done when a repository lacks it. `branch list` and
`branch close` read every repository of the World, the other commands the repositories in scope.

### Commands

| `[CommandPath]` | Purpose |
|---|---|
| `branch list [--link/-l]` | **Handled by [`CKli.HotZone.Plugin`](../CKli.HotZone.Plugin/README.md#how-branch-sync-and-branch-list), implemented here** (`DisplayBranchList`). Displays the opened branches of the World as an indented tree (`BranchNamespace.GetDisplayBranches`), each one prefixed by its link type's compact code, followed by the legend that maps each code to the name the configuration and `--link` use. A column summarizes where each branch has changes and, between a branch and its parent, a `↖` line tells what `branch close` would merge into the parent and a `↘` line what `branch sync` would merge into the branch: see [What `branch list` reads](#what-branch-list-reads). `--link` (`Regular`/`CI`/`Full`) predicts the synchronizations with that link instead of each branch's configured one. World-global: it reports the `BranchNamespace`, not the Git branches of the repositories (that is `ckli issue`). |
| `branch open <branchName> [--link/-l] [--parent] [--dry-run/-d]` | **Handled by [`CKli.HotZone.Plugin`](../CKli.HotZone.Plugin/README.md#how-branch-sync-and-branch-list), implemented here** (`OpenBranch`). Opens (or updates the link type of) a pre-release or `explo/` branch: updates the `BranchNamespace`, then runs `HotBranch.OpenOrPredict` in every repo under the current path. A missing branch is created at the commit its link propagates, so it has nothing to merge; an already opened branch is synchronized exactly like `branch sync` does it (package versions aligned, other conflicts left in progress on the `dev/` branch). The `dev/` branch is checked out. `--link` (`Manual`/`Regular`/`CI`/`Full`) defaults to `Full` for a new branch and leaves an already opened branch's link type unchanged. Only the branch and its closest existing parent matter: a repository where one of them has an issue is skipped and fails the command, the others are still opened. A failed command doesn't save the `BranchNamespace` (no failed command saves the World's definition file): opening the branch again once the failures are fixed completes it, the already created branches are kept. `--dry-run` creates, merges and saves nothing: it displays what the open would do and fails when the open would fail. |
| `branch close <branchName> [--discard] [--dry-run/-d]` | **Handled by [`CKli.HotZone.Plugin`](../CKli.HotZone.Plugin/README.md#how-branch-sync-and-branch-list), implemented here** (`CloseBranch`). Integrates the branch into the `dev/` branch of its closest *existing parent* branch (via `HotBranch.Close`) in the repos under the current path, and removes it from the namespace once no repo of the World has it anymore. See [Closing a branch](#closing-a-branch). `--discard` skips the Git-side integration and only edits the namespace: it must be run at the World root. `--dry-run` merges and deletes nothing: it displays what the close would do and fails when the close would fail. |
| `branch switch <branch> [--create/-c] [--all]` | **Handled by [`CKli.HotZone.Plugin`](../CKli.HotZone.Plugin/README.md#how-branch-sync-and-branch-list), implemented here** (`SwitchBranch`). Checks out `branch` (or its `dev/` branch if it exists) in the current/all repos, or its closest existing branch where it doesn't exist. `--create` opens it instead, exactly like `branch open` opens an already opened branch (`HotBranch.OpenOrPredict`: created if needed, synchronized, its `dev/` branch checked out, conflicts handled the same way) but never changes the `BranchNamespace`: the branch must be in it, which is how the root is reached (`branch switch dev/stable -c`). |
| `branch sync <branch> [--link/-l] [--all] [--dry-run/-d]` | **Handled by [`CKli.HotZone.Plugin`](../CKli.HotZone.Plugin/README.md#how-branch-sync-and-branch-list), implemented here** (`SynchronizeBranch`): the package versions that conflict are resolved the way a build of the branch updates them. When other conflicts remain, the merge is left in progress in the working folder, the versions already aligned (the `dev/` branch is checked out, `ckli status` shows `(merging, N conflicts)`): resolve the conflicts and commit the merge with any Git tool, or abort it. `--dry-run` merges nothing: it displays what the synchronization would do (from the local branches) and fails when the synchronization would fail. Runs `HotBranch.Synchronize` for `branch` in the current/all repos, optionally overriding the configured `LinkType` with `--link` (`Regular`/`CI`/`Full`). Only the branch and its closest existing parent matter (`HotBranch.CheckNoIssue`): a repository where one of them has an issue is skipped and fails the command; the others are still synchronized. The root has no parent: `--link` cannot be used with it. When `branch` (or its `dev/`) was checked out, its `dev/` branch is checked out afterwards: a merge can create it, and the auto fix of a useless `dev/` moves the checkout to the base branch. |
| `commit <message> [--all]` | Commits any pending changes in the current/all repos (no-op if nothing changed). |

All commands accept the standard `IActivityMonitor` + `CKliEnv` prefix; repository selection
follows the usual CKli convention (current directory's repo(s), or `--all`/`all` for the whole
World). `TryParseBranchFixName` is a small static helper (`fix/vMAJOR.MINOR` parsing) kept here for
reuse by the Fix Workflow machinery in `CKli.HotZone.Plugin`.

### Closing a branch

`branch close mike` works on the repositories under the current path, whatever the other repositories
and the other branches are:

- **Downstreams may keep the branch.** Closing an upstream first is safe: a downstream keeps consuming the
  last `mike` version of it until its next `mike` build, which reads the upstream from its closest existing
  branch and moves onto the next parent version (greater than the `mike` prerelease).
- **Upstreams whose `mike` versions are consumed are closed too.** Closing a downstream alone would
  integrate a reference to a `mike` version into its parent, and nothing would ever heal it: the upstream's
  parent never receives the changes that version carries. So the scope is extended, transitively, to the
  repositories that have `mike` and produce a package that a closed `mike` solution consumes in a `mike`
  version (`BranchName.Match`). Each addition is announced (`Also closing 'mike' in 'X-Core': its 'mike'
  versions are consumed by 'X-App'.`). An upstream whose `mike` versions nobody consumes is left alone.
- **Only the closed branch and the branch that receives it matter**, and only in the closed repositories: an
  issue on `juliet` (a child of `mike`) or in another repository doesn't block. Any issue on `mike` or on its
  closest existing parent in a closed repository fails the command before anything is integrated.
- **The namespace keeps `mike` while a repository of the World still has it**: the command then reports
  `Branch 'mike' closed in N repositories, still opened in M.`
- **The content goes to the parent's `dev/` branch**, never to its base branch, which only moves when that
  `dev/` branch is integrated by a regular build.
- **The merge is the one of `branch sync`**, resolved for the parent: the package versions that conflict are
  resolved the way a build of the parent (the branch that receives the merge) updates them. A merge that
  conflicts beyond them is left in progress on the parent's `dev/` branch (checked out for this), `mike` stays
  opened in that repository and the command fails: once the merge is committed, `branch close mike` again
  finds it merged and completes the close. The other repositories are closed regardless.
  `--dry-run` displays what the close would do and fails when it would fail, changing nothing.

This is local: `HotBranch.Close` deletes the local branches only (`DeleteGitBranchMode.WithTrackedBranch`),
nothing reaches a remote before a `ckli push`.

### What `branch list` reads

```
Opened branches of 'Test':
Branch        Repositories
stable        4 repositories
  => bravo    No change, 4 unchanged.
       ↖ 2 fast-forwards
    => alpha  X-Core and 1 other repository, 2 unchanged, weight: 3 repositories, 3 projects.
```

The root row counts the World's repositories. Every other row lists the **tips** of the repositories
where the branch has changes (the ones none of whose upstreams has changes), then how many other such
repositories there are, the number of repositories where the branch is opened but **unchanged**, and the
**weight** of the branch: the repositories and projects (all the projects of their solutions) that a build of
the branch touches. That is the repositories with changes and all their downstreams, unchanged ones included:
above, X-Middle is unchanged but weighs, since it is updated with X-Core's new version.

- **Unchanged is about content, not commits**, and this notion exists only here: a branch is unchanged when the
  tree of its tip (`GitDevBranch ?? GitBranch`) is the tree of its merge base with its closest existing
  parent. The empty merge commits of a synchronization and the "Producing 'vX' from unchanged head."
  commits bring nothing to a reader of the system. This is deliberately the opposite of the
  [useless `dev/` branch](#issue-detection-worldeventsissue-and-ckli-issue) rule, which counts commits so
  that a version is never orphaned.
- **Upstreams are transitive and every repository is read.** A repository where the branch doesn't exist
  is read from its closest existing branch: with X-Core ← X-Middle ← X-App and changes in X-Core and X-App
  only, X-App is not a tip.
- **No `HotGraph` for the summary**: `CKli.HotZone.Plugin` depends on this plugin, and a `HotGraph` refuses a
  World with issues. The relation is computed from the shallow solutions alone, with the HotGraph's project
  name to package identifier heuristic (an explicitly non packable project produces nothing).

The edge between a branch and its parent carries what the two merges between them would do, right above the
branch and aligned on its name: `↖` what `ckli branch close` would merge into the parent, then `↘` what
`ckli branch sync` would merge into the branch. The arrows point at the branch that receives the merge:

```
Opened branches of 'Test':
Branch       Repositories
stable       4 repositories
     ↖ 1 merge, 1 conflict (X-Conflict)
     ↘ 1 fast-forward, 1 merge, 1 conflict (X-Conflict)
  => sierra  X-Merge, X-Conflict, 2 unchanged, weight: 2 repositories, 2 projects.
```

A line appears only when its merge has something to do somewhere, so an up to date branch keeps a single row,
and the `Merges:` legend that names the two commands appears only when there is at least one line. The merge
lines are single cells that span the whole table line: they don't widen the `Branch` column. The header is the
table's first row (underlined): `ColumnDefinition` has a header but `TableLayout` doesn't implement it.

Both lines are the commands' own predictions, computed in the object database only, in every repository where
the branch and a parent exist: `HotBranch.PredictClose` (the branch's tip into the `dev/` branch of its closest
existing parent) and `HotBranch.PredictSynchronize` (the link commit into the branch's `dev/` branch). Each
repository is up to date (nothing to merge: nothing is shown), a **fast-forward**, a **merge**, or a
**conflict**. A merge that conflicts on package versions only is computed the way the command resolves it
(`PackageVersionMerge`, with the resolver from the `HotGraph` of the branch that receives the merge, requested
once per branch when a merge first conflicts): it is a merge. When the `HotGraph` cannot be obtained (a World
with issues, a solution that cannot be read), the errors that explain it are displayed once, followed by a
single warning for the branch, and its conflicts stay conflicts. Conflicts need someone, so they name their
repositories (in red); so do the **unknown** ones (yellow), where the merge cannot be computed, typically
because the commit to integrate cannot be found: a Regular or CI link without an `ITagCommitProvider`, or a
repository whose version tags have issues (the errors of the `ITagCommitProvider` say which, and `ckli issue`
reports them). These names are `Repo.ToInlineNameRenderable`: linked to the working folder and preceded by `✱`
when the repository is dirty, since a dirty working folder may be why its merge conflicts.

- **`--link` changes the `↘` lines only.** It predicts the synchronizations with that link type instead of each
  branch's configured one, exactly like `branch sync --link`, and it cannot be `Manual`. A close doesn't
  depend on the link: it always merges the whole branch.
- **Only the link to the parent is considered.** The merges of the `origin/` branches that a
  synchronization starts with depend on a fetch: that is `ckli pull`'s business.
- `Manual` links propagate nothing (without `--link`, they have no `↘` line) and the root has no parent: it
  has no edge.

### Issue detection: `World.Events.Issue` and `ckli issue`

`BranchModelPlugin` subscribes to `World.Events.Issue`. For every repository in scope it:

1. Calls `BranchModelInfo.CollectIssues`, which walks every `HotBranch` and, through a
   `BranchIssueBuilder`, accumulates:
   - **`MissingRootBranchIssue`** — the root branch doesn't exist; if a conventional previous-root
     candidate is found (`stable`, `main`, `master`, `root`, `trunk`, `mother`, `primary`,
     `develop`, or an existing `dev/<root>`), it can be auto-created from it, otherwise it's a
     manual issue.
   - **`MissingBaseBranchesIssue`** — one or more `dev/` branches whose base branch is missing (a
     severe issue: the repository cannot be built); fixed by recreating the base where the `dev/` branch
     left its parent.
   - **`RemovableBranchesIssue`** — one or more `Useless` `dev/` branches; fixed by deleting them.

   Both are *implicit* (Ⓘ) when `AutoFixDevBranch` is true: `ckli issue` itself fixes nothing, so it
   reports them, but every other command fixes them silently when it reads the repository. When it is
   false, they are automatic issues (⚙) for `ckli issue --fix`.
   - **`DesynchronizedBranchesIssue`** — `Desynchronized` branches that can be auto-merged without
     conflict; a separate manual `World.Issue` is raised for the ones that can't.
   - Manual-only issues for `Unrelated` and `DesynchronizedCheckout` links.
2. If no *severe* branch issue was found for a repository, raises the `ContentIssue` event
   (`ContentIssueEventArgs`, one per existing `HotBranch`) so other plugins can inspect that
   branch's content (via `Content`/`GetContentFiles`, or `TryGetSolution` for the `.slnx`) and
   report their own issues through `ContentIssueEventArgs.Issues` (a `Collector`):
   `ManualFix`, `DeleteFile(Folder)`, `MoveFile(Folder)` (case-fix aware — a rename is applied as a
   two-step move to survive case-insensitive filesystems), `CreateFile`/`UpdateFile`.
   `ContentIssueBuilder` wraps all per-branch collectors for a repo into one `WorldIssue`, whose fix
   checks out each `dev/` branch (creating it if needed), applies the recorded file
   operations, and commits — restoring the original checked-out branch afterwards regardless of
   outcome.

This is the same "detect now, fix later, on demand" pattern used by `CKli.Build.Plugin`'s tag
issues: `World.Issue` subclasses only *describe* the problem when collected; `ExecuteAsync` does
the actual Git/file work, and only runs when the user asks `ckli issue` to fix it.

### Notable internal details

- `BranchModelPlugin.Create` special-cases `ckli issue` itself (`PrimaryPluginContext.Command is
  CKliIssue`): auto-fixing useless `dev/` branches is disabled while *detecting* issues, so the
  `issue` command reports what it would fix without silently fixing it first.
- A name designating a `dev/` branch passed to `branch switch`/`branch sync` is recognized
  (`GetReposAndBranch`) and normalized before namespace lookup, but still causes the command to
  target/ensure the `dev/` branch specifically. `BranchNamespace.RemoveDevPrefix` accepts every spelling:
  the command form `dev/X` (and `dev/@lts/X` in a LTS World) as well as the actual git name, `dev/X` or
  `@lts/dev/X`. The Build plugin uses it too, to resolve the branch to build from the checked out ones.
- `BranchLink.CreateAheadBranch` prefers reusing an existing `origin/dev/xxx` remote branch over
  creating a fresh local one, and can add an empty "Initializing '...'" commit when there is no
  remote to base the new `dev/` branch on (needed because a `dev/` branch identical to its base is
  otherwise indistinguishable from "nothing to build").
