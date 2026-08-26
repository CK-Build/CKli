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
`BranchModelPlugin.SetTagCommitProvider(this)` in its constructor, so that `BranchLinkType.Release`
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
`Name`), an `Index` (used to align with per-repo arrays), a `Parent` (null only for the root), a
`LinkType` describing how it is kept in sync with its parent, and a `VersionKind`
(`CSVersionKind`, from CSemVer — `Stable`, `Alpha`..`Zulu`, or `Exploratory`).

A **`BranchNamespace`** owns the whole tree for a World:

- **`Root`** — the single stable branch (`stable` by default, configurable, prefixed with the
  World's `WorldName.LTSName` in an LTS world, e.g. `net8/stable`).
- **`MainLineBranches`** — the root followed by the opened CSemVer pre-release branches
  (`alpha`..`zulu`), ordered from least to most stable, each with its own `LinkType`. This is what
  the `MainLine` XML attribute encodes.
- **`ExploratoryBranches`** — `explo/name` branches, each anchored under a main-line branch or
  under another `explo/` branch (arbitrary nesting), configured as `<Explo>` child elements.

`BranchNamespace` is immutable: `AddOrUpdate`, `AddOrUpdateExplo` and `Remove`
(`BranchNamespace.Mutators.cs`) all return a *new* namespace (plus the affected `BranchName`)
rather than mutating in place. `BranchModelPlugin` persists the result back to the World's XML via
`SaveBranchNamespace` after any command that mutates it.

#### Configuration XML

```xml
<Plugins>
  <BranchModel MainLine="stable |> beta -> rc" AutoFixUselessBranch="true">
    <Explo Name="explo/spike-x" Parent="beta" Link="Manual" />
    <Explo Name="explo/spike-x-sub" />  <!-- nested under explo/spike-x -->
  </BranchModel>
</Plugins>
```

- **`MainLine`** (attribute, optional) — `"<root> <link> <prerelease> <link> <prerelease> ..."`.
  Prereleases must be given in increasing CSemVer stability order and are conformant SVersion
  prerelease names (`alpha`, `bravo`, ... `zulu`). Defaults to `"stable"` alone (only the root
  branch open) when absent.
- **`AutoFixUselessBranch`** (attribute, optional, default `true`) — when a branch's `dev/` exists
  but has nothing ahead of its base (a "useless" `dev/`), silently delete it instead of reporting
  it as an issue.
- **`<Explo Name="..." Parent="..." Link="...">`** — an exploratory branch. `Name` must be
  `explo/<lowercase-id>` (the `explo/` prefix and the LTS prefix are inferred if omitted). `Parent`
  is required only on a *root* `<Explo>` (nested `<Explo>` elements inherit their XML parent).
  `Link` defaults to `CI`.

`GetMainLine()` / `GetExplo()` serialize back to this same shape; `GetDisplayTree()` renders the
whole tree indented for display (`ckli issue` and friends use `ToParentedString()` for a single
branch).

#### Link types: how a `dev/` child is kept in sync with its parent

| `BranchLinkType` | Code | Meaning |
|---|---|---|
| `None` | *(root only)* | Not applicable — the root branch has no parent. |
| `Manual` | `\|✋` | No propagation at all; the `dev/` branch must be updated by hand. |
| `Release` | `\|>` | The parent's **stable/pre-release tagged commits** are merged in — a build must have produced a *release* on the parent to reach this branch. |
| `CI` | `->` | *(default)* The parent's **built commits** (including CI builds) are merged in — any build (`build`/`build --ci`) on the parent reaches this branch. |
| `Full` | `=>` | The parent's **`dev/` tip** is merged in directly, regardless of whether it was ever built. |

`Release`/`CI` synchronization needs to know "what was last built on the parent" — this is exactly
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
| `Useless` | `Ahead` has no commit beyond `Branch` (or identical tree) — should be deleted (auto-fixed unless checked out and dirty, see `AutoFixUselessBranch`). |
| `Unrelated` | `Ahead` shares no common ancestor with `Branch` — must be fixed manually. |
| `Desynchronized` | `Ahead` is behind `Branch` — fixable by merging `Branch` into `Ahead`. |
| `DesynchronizedCheckout` | Same as above, but `Ahead` is checked out and the working folder is dirty — cannot be auto-merged. |

`HotBranch` exposes the operations that keep this model consistent:

- **`EnsureExists`** — creates the branch from the closest existing ancestor if missing.
- **`Close`** — integrates `dev/` into the branch, then merges the branch into its closest existing
  parent and deletes it (used to retire a pre-release line).
- **`Synchronize(monitor, applyLink)`** — the core propagation logic: merges tracked (`origin/...`)
  branches first, resolves `Desynchronized`/`Useless` locally, then — unless the link is `Manual`
  or `None` — recursively synchronizes the parent and merges the right thing into `dev/` according
  to `LinkType` (`Full`: parent's `dev/` tip; `Release`/`CI`: the commit from `ITagCommitProvider`).
- **`Commit`** / **`IntegrateDevBranch`** / **`EnsureDevBranch`** — everyday `dev/` branch
  operations (development always happens on `dev/`; only integration touches the base branch).

`BranchModelInfo.GetClosestExistingBranch` / `GetRequiredClosestExistingBranch` walk up `Parent`
links to find the nearest branch that actually exists in Git — used whenever an operation needs
"the branch to act on" but the exact target hasn't been created yet.

### Commands

| `[CommandPath]` | Purpose |
|---|---|
| `branch open <branchName> [link] [parent]` | Opens (or updates the link type of) a pre-release or `explo/` branch: updates the `BranchNamespace`, then creates/synchronizes the corresponding `dev/` branch in every repo under the current path and checks it out. |
| `branch close <branchName> [--discard]` | Retires a branch World-wide: integrates it into its closest parent (via `HotBranch.Close`) in every repo, then removes it from the namespace. Must be run at the World root; `--discard` skips the Git-side integration and only edits the namespace. |
| `branch switch <branch> [--create/-c] [--all]` | Checks out `branch` (or its `dev/` branch if it exists) in the current/all repos; `--create` first ensures the branch exists and synchronizes it. |
| `branch sync <branch> [mode] [--all]` | Runs `HotBranch.Synchronize` for `branch` in the current/all repos, optionally overriding the configured `LinkType` with `mode` (`Release`/`CI`/`Full`). |
| `commit <message> [--all]` | Commits any pending changes in the current/all repos (no-op if nothing changed). |

All commands accept the standard `IActivityMonitor` + `CKliEnv` prefix; repository selection
follows the usual CKli convention (current directory's repo(s), or `--all`/`all` for the whole
World). `TryParseBranchFixName` is a small static helper (`fix/vMAJOR.MINOR` parsing) kept here for
reuse by the Fix Workflow machinery in `CKli.HotZone.Plugin`.

### Issue detection: `World.Events.Issue` and `ckli issue`

`BranchModelPlugin` subscribes to `World.Events.Issue`. For every repository in scope it:

1. Calls `BranchModelInfo.CollectIssues`, which walks every `HotBranch` and, through a
   `BranchIssueBuilder`, accumulates:
   - **`MissingRootBranchIssue`** — the root branch doesn't exist; if a conventional previous-root
     candidate is found (`stable`, `main`, `master`, `root`, `trunk`, `mother`, `primary`,
     `develop`, or an existing `dev/<root>`), it can be auto-created from it, otherwise it's a
     manual issue.
   - **`RemovableBranchesIssue`** — one or more `Useless` `dev/` branches, or `dev/` branches whose
     base branch is entirely missing; fixed by deleting them.
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
- A `"dev/"`-prefixed branch name passed to `branch switch`/`branch sync` is recognized
  (`GetReposAndBranch`) and stripped before namespace lookup, but still causes the command to
  target/ensure the `dev/` branch specifically.
- `BranchLink.CreateAheadBranch` prefers reusing an existing `origin/dev/xxx` remote branch over
  creating a fresh local one, and can add an empty "Initializing '...'" commit when there is no
  remote to base the new `dev/` branch on (needed because a `dev/` branch identical to its base is
  otherwise indistinguishable from "nothing to build").
