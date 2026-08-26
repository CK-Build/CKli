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

## How: the Fix Workflow

A **Fix Workflow** targets a version that already fell out of the hot zone (an old stable release)
and needs a patch, without going through the regular `build`/`publish` flow. It is started, inspected
and cancelled with three commands exposed by `HotZonePlugin`:

| Command | Description |
|---|---|
| `fix start <version> [--move-branch] [--with-empty-commit]` | Starts (or restarts) a Fix Workflow for the given `Major` or `Major.Minor` version of the current repo. |
| `fix info` | Dumps the current Fix Workflow, if any. |
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
