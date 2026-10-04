# CKli.HotZone.Plugin

**CKli.HotZone.Plugin** builds the cross-repository dependency graph of a World and drives the
workflow for patching an already-released version ("fixing"). It is the plugin that turns the
per-repository information exposed by `VersionTagPlugin`, `BranchModelPlugin` and `ShallowSolutionPlugin`
into a single, ordered, buildable graph — the foundation used by `CKli.Build.Plugin` and
`CKli.Publish.Plugin` to know what to build, in what order.

## Why: the "hot zone"

`VersionTagInfo.HotZoneInfo` (in `CKli.VersionTag.Plugin`) calls the "hot zone" of a repository the
set of tagged commits between its last published stable version (`LastStable`) and the most recent
tag (`TopHot`, possibly a `local/`, CI or `+fake` version). It is, literally, everything that has
happened *since* the last stable release — the zone that is still "hot" (unreleased, moving).

`CKli.HotZone.Plugin` doesn't compute the hot zone itself (that's `VersionTagPlugin`'s job); it
*consumes* it to answer two different but related questions:

- **"What must be built, in what order, across the whole World?"** — answered by `HotGraph`, a
  dependency graph over all repositories' solutions, used to build/publish the *current* hot zone.
- **"How do I ship a fix for a version that is *not* in the hot zone anymore (an older stable
  release), and safely propagate it downstream?"** — answered by `FixWorkflow`, an explicit,
  persisted, multi-repository workflow started with `ckli fix start`.

Both concerns share the same underlying package/dependency-graph machinery (`VersionTagPlugin`,
`ShallowSolutionPlugin`), which is why they live in the same plugin.

## How: `HotGraph`

`HotZonePlugin.GetHotGraph(monitor, branchName, isCIBuild, pivots)` builds a `HotGraph`: it reads,
for every `Repo` of the World, the closest existing branch to `branchName` (see `BranchModelPlugin`),
loads its `.slnx` content via `ShallowSolutionPlugin`, and links solutions together by matching
produced/consumed package identifiers.

- **`HotGraph.Solution`** wraps one repository's solution in the graph. It exposes `Rank` (build
  order — 0 first), `DirectRequirements` / `AllRequirements` (the solutions it depends on),
  `IsPivot` / `IsPivotUpstream` / `IsPivotDownstream`, and `IsDevSolution` (whether the solution was
  read from a `dev/` branch instead of the regular one).
- **Pivots** are the repositories that triggered the graph computation (e.g. the repos being
  built/published). When pivots are specified, `HotGraph.TopologicalSort` first ranks the pivots and
  their transitive dependencies (`isPivotUpstream`), then ranks everything else as pure downstream
  consumers. When no pivot is specified, every repository is considered a pivot.
- **`dev/` branches**: a solution can be read from its `dev/<branch>` branch instead of the regular
  one. `ConsiderBuildImpact` promotes solutions to `DevSolutions` on demand — this is how "building
  repo A" can pull in the `dev/` branch of a downstream consumer B without requiring the whole World
  to be on `dev/`.
- **`HotGraph.PackageUpdater`** (`GetPackageUpdater`) computes, once every `VersionTagInfo` is
  issue-free, the package-version mappings a build/publish operation needs:
  - `GetAlreadyBuiltMapping(ciBuild)` — versions already published for solutions that don't need a
    rebuild (`SolutionVersionInfo.LastBuiltVersion.VersionMustBuild` is false and there is no code
    change).
  - `WorldConfiguredMapping` — the World's externally-configured package versions
    (`VersionTagPlugin.GetPackagesConfiguration`).
  - `DiscrepanciesMapping` — resolves version mismatches on the same external package referenced by
    different solutions, by picking the greatest referenced version.
- **`SolutionVersionInfo`** (per `Solution`, computed from its `VersionTagInfo`) exposes
  `GetLastBuild(ciBuild)`, whose `VersionMustBuild` / `HasCodeChange` flags tell the build plugin
  whether a solution can be skipped.

`BuildIndexAndRankDisplayState` is a small `ref struct` helper shared by the graph and the fix
workflow renderers to draw the build-index / rank-range gutter (`╓`, `║`, `╙`) seen in `ckli`'s
console output.

`HotZonePlugin.CheckBasicPreconditions(monitor, plannedAction, out allRepos)` is a second, simpler
entry point (not a `[CommandPath]` command) that other plugins call before relying on the graph or
starting a workflow: it checks that no repo is dirty and that no repo's `VersionTagInfo` or
`BranchModelInfo` currently has an issue, logging one error per offending repo. `CKli.Build.Plugin`
calls it from `BuildPlugin.Fix.cs` before building a Fix Workflow.

## How: `branch sync` and `branch list`

These commands, and `branch close`, are implemented by `CKli.BranchModel.Plugin` (`BranchModelPlugin.SynchronizeBranch`,
`DisplayBranchList` and `CloseBranch`, documented in [its README](../CKli.BranchModel.Plugin/README.md#commands)) but
handled here (`HotZonePlugin.Branch.cs`): a command is not tied to the plugin that implements its feature, and these
ones need a `HotGraph`. `branch close` merges a branch into its parent: its versions are resolved with the parent's
`HotGraph`, the branch that receives the merge.

Two branches that are built independently both rewrite the references to the World's packages, so merging
a parent's build into its child typically conflicts on the very same `<PackageReference Version="..." />`
lines, each side referencing its own build. These conflicts are resolved the way a build of the branch updates the
references of a solution it doesn't rebuild: `HotGraph.PackageUpdater.CreateVersionResolver( ciBuild )` maps a
package with `GetAlreadyBuiltMapping` (a World package: its producer's last build *for this branch*, the
closest branch toward the root), then `WorldConfiguredMapping` (the World's bounds), and defaults to the
greatest of the two versions (the `DiscrepanciesMapping` rule applied to the two sides). The merges go to the
`dev/` branches, so the graph is the CI one.

`CreateVersionResolverProvider` computes the `HotGraph` of a branch the first time a merge of that branch
conflicts on project files and caches it, failure included: a sync or a list without such a conflict never
computes it, and a World with issues (that has no `HotGraph`) still synchronizes everything that merges
cleanly. The aligned merge itself is `ShallowSolution`'s
[`PackageVersionMerge`](../CKli.ShallowSolution.Plugin/README.md#packageversionmerge): any conflict beyond the
package versions is a real conflict.

A real conflict needs a person, and a person needs a merge to work on: by default, `branch sync` leaves the merge
in progress in the working folder (`GitRepository.PrepareMerge`), its package versions already aligned, so that
any Git tool shows only the real conflicts. The `dev/` branch is checked out for this (the person is about to work
on that merge), `ckli status` shows `(merging, N conflicts)`, and the merge that the person commits has the two
original commits as parents; aborting it restores the branch as it was. The synchronization of that repository
fails until the merge is committed: the next `branch sync` then finds it up to date. `branch sync --dry-run`
merges nothing: it computes the merges in the object database (`HotBranch.PredictSynchronize`, from the local
branches: the merges of the remote branches depend on a fetch and are not predicted), displays a summary of their
outcomes and the merges that would be left in progress, and returns what the synchronization would return - so
that a script can test it. `branch close --dry-run` does the same with `HotBranch.PredictClose`. The reading of the
branch model is not inert, though: the `AutoFixDevBranch` repairs still happen. `branch close` prepares its merge the same way, on the parent's `dev/` branch: the
branch stays opened until the merge is committed and the close is run again.

The merges left in progress are reported to the command (`PreparedMerge`, through the `onPreparedMerge`
callback of `HotBranch.Synchronize` and `HotBranch.Close`) and displayed together at its end, one collapsable
per repository: the repository (linked to its working folder), the checked out branch, what is merged and the
number of conflicts, above the paths in conflict.

```
A merge is left in progress: resolve its conflicts and commit it (or abort it).
> X-Core  ⎇ dev/sierra ← branch 'dev/stable'  1 conflict
│ Conflict.txt
``` Only these two commands prepare
merges: `branch open`, `branch switch --create` and the roadmap fail on a conflict.

The roadmap doesn't need this: its `Synchronize` call (`BuildPlugin.RoadmapExecutor`) runs right after
`EnsureExists` created the branch at its start commit, which is the link commit itself.

## How: the Fix Workflow

A **Fix Workflow** targets a version that already fell out of the hot zone (an old stable release)
and needs a patch, without going through the regular `build`/`publish` flow. It is started, inspected
and cancelled with three commands exposed by `HotZonePlugin`:

| Command | Description |
|---|---|
| `fix start <version> [--move-branch] [--with-empty-commit]` | Starts (or restarts) a Fix Workflow for the given `Major` or `Major.Minor` version of the current repo. |
| `fix info` | Dumps the current Fix Workflow, if any. |
| `fix push` | Pushes the targets' `fix/` branches to their remote (creating them if needed) so that another developer can join the fix. The `local/` version tags are deliberately not pushed — see [`Fix-Workflow.md`](../Fix-Workflow.md#sharing-a-fix-in-progress). |
| `fix cancel` | Cancels the current Fix Workflow and destroys the local releases it may have created. |

`fix start`:
1. Resolves the exact stable commit to fix (`toFix`) from the local repo's `fix/vMajor.*` tags and
   refuses if that version is actually inside the hot zone (in that case, the regular
   `build`/`publish` commands must be used instead).
2. Ensures a `fix/vMajor.Minor` branch exists (or is moved, if `--move-branch` is passed) on the
   commit to fix, in the current repo (the *origin*) and, recursively, in every downstream consumer
   that has a `LastMajorMinorStables` tag on the version being fixed (`FixWorkflow.TargetRepo.Rank`
   tracks the propagation depth so unrelated targets can be built in parallel).
3. Raises `HotZonePlugin.OnFixStart` (a `PerfectEvent<FixWorkflowStartEventArgs>`) so other plugins
   can prepare or validate the target branches before the workflow is persisted.
4. Persists the resulting `FixWorkflow` (an ordered `ImmutableArray<FixWorkflow.TargetRepo>`, one
   `.bin` file per World) to `$Local/<world>.FixWorkflow.bin`, so `fix info` / `fix cancel` (and,
   outside of this plugin, `ckli fix build` / `ckli fix publish`) can pick it back up. State besides
   this file is never trusted: the workflow only records *what* to build, not *whether* it has been
   built — that is derived live from the repositories at build time.

`FixWorkflow` is deliberately append-only/immutable once started: it is up to consumers (`fix build`,
`fix publish` — implemented elsewhere) to verify at each step that a fix build doesn't accidentally
change a produced package identifier or bump a dependency outside the fix's own scope.

## Plugin wiring

`HotZonePlugin` derives from `PrimaryPluginBase` (see [CKli.Core's README](../../CKli.Core/README.md#plugin-system))
and is constructor-injected with `BranchModelPlugin`, `VersionTagPlugin`, `ShallowSolutionPlugin` and
`ArtifactHandlerPlugin`. Its `.csproj` only lists a `ProjectReference` to `CKli.VersionTag.Plugin`;
the other plugin types are reached transitively — `VersionTag.Plugin` → `ArtifactHandler.Plugin`, and
so on down the `StandardPlugins` dependency chain — which is the general pattern used across these
plugin projects rather than each one listing every plugin it happens to use.
