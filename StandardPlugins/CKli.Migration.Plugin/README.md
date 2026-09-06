# CKli.Migration.Plugin

## WHY

CKli's own convention for a Repo evolved over time (branch model, version tagging,
build tooling, solution file format). Repos and Stacks created under the older
"Net8-era" convention carry artifacts of that older world: a `master`/`develop`
branch pair (instead of the current `stable`/`dev/stable` branch model), a
`RepositoryInfo.xml` + `CodeCakeBuilder/` + `.sln` build setup (instead of the
current build pipeline and `.slnx` solution), a hand-maintained `InfVersion`, and
local NuGet source entries left over from local testing.

`CKli.Migration.Plugin` is the place these one-off, repo-shape-changing migrations
live. It is explicitly called out in the source as a **"very optional plugin"**
containing **temporary utilities** — it is not meant to be part of every World's
plugin set, only enabled transiently on a Stack that still needs to be walked
through the Net8 → Net10 conversion. Once a Stack's repos are converted, the
plugin has nothing left to do.

It sits on top of (and depends on) several other Standard Plugins rather than
duplicating their logic: `ArtifactHandlerPlugin` (local NuGet/Assets folders),
`VersionTagPlugin` (InfVersion / local release tags), `BranchModelPlugin`
(`stable` / `dev/stable` branches), `HotZonePlugin` (the fix workflow whose
`OnFixStart` event it hooks), and `BuildPlugin` (referenced but currently unused
by the migration logic itself). Because it is a `PrimaryPluginBase`, all of these
are pulled in via constructor injection and are always instantiated together with
it.

## HOW

### Type

```csharp
public sealed class MigrationPlugin : PrimaryPluginBase
```

`MigrationPlugin` derives from `PrimaryPluginBase`, so it receives a
`PrimaryPluginContext` and is always instantiated for a World that declares it —
there is no per-Repo `RepoInfo` involved. Its constructor takes the
`PrimaryPluginContext` plus the four collaborating plugins listed above (and
`BuildPlugin`, stored but not otherwise used):

```csharp
public MigrationPlugin( PrimaryPluginContext primaryContext,
                        ArtifactHandlerPlugin artifactHandler,
                        VersionTagPlugin versionTag,
                        BranchModelPlugin branchModel,
                        HotZonePlugin hotZone,
                        BuildPlugin build )
```

There is no `<MigrationPlugin>` configuration element — the plugin has no
persisted configuration, per-World or per-Repo.

### `HotZonePlugin.OnFixStart` subscription

In the constructor it subscribes to `hotZone.OnFixStart` (a
`PerfectEvent<FixWorkflowStartEventArgs>` raised by `HotZonePlugin.FixStartAsync`
when a `ckli fix start` workflow begins):

```csharp
void OnFixStart( IActivityMonitor monitor, FixWorkflowStartEventArgs e )
{
    if( e.RestartingWorkflow ) return;   // only react on the initial start
    foreach( var target in e.Targets )
    {
        // checkout target.BranchName in target.Repo, then:
        RemoveRepositoryInfoAndCodeCakeBuilderAndSlnx( monitor, repo );
        repo.GitRepository.Commit( monitor, "Net8 migration applied." );
    }
}
```

For each `FixWorkflow.TargetRepo` in the roadmap (skipped when the fix workflow
is merely restarting, since the work was already done on the initial start), it
checks out the target branch and strips the legacy Net8 build files, committing
the result as `"Net8 migration applied."`. This lets the normal `ckli fix`
workflow silently carry a repo's legacy branches across the migration as part of
its regular fix-up pass.

### Command

```
[CommandPath("maintenance migrate net8")]
bool MigrateNet8( IActivityMonitor monitor, bool hardResetAll = false, bool restoreRemotes = false )
```

| Parameter | Meaning |
|---|---|
| `hardResetAll` | Deletes every repo folder under the World root (except the `-Stack` folder itself) before proceeding, so `World.GetAllDefinedRepo` re-clones everything from scratch. |
| `restoreRemotes` | For each repo: drops the local `develop`/`stable`/`dev/stable` branches and local-only version tags, re-fetches `develop` from the remote, and wipes/recreates the local NuGet (`ArtifactHandlerPlugin.LocalNuGetPath`) and Assets (`LocalAssetsPath`) folders. Used to discard local experimentation and restart the migration from a clean, remote-tracked state. |

`MigrateNet8` runs the full one-shot Net8 → Net10 conversion for the whole
Stack, in order:

1. **`CheckoutMasterIfItExistsAndFetchTags`** — fetches remote branches (no
   prune), checks out `master` where it still exists, and fetches tags for
   every repo regardless.
2. **`InitializeInfVersionFromMaster`** — must run *before* `RepositoryInfo.xml`
   is deleted. For each repo currently on `master` it infers an `InfVersion`
   from whichever is higher of: the version implied by the tip of a
   `master-Net6` branch (via `git describe`, patch bumped by one), or the
   `<SimpleGitVersion StartingVersion="…">` recorded in `RepositoryInfo.xml`.
   The computed minimum is projected to an `InfVersion` (a `Major.Minor.Patch-0`
   prerelease, or `null` when the minimum is `0.0.0`) and stored via
   `versionTag.SetInfVersion`. This bridges repos that predate automatic
   `InfVersion` computation on LTS creation (see `VersionTagPlugin.ComputeRepoLTSVersions`).
3. **master → `stable` branch model** (idempotent): ensures each repo's
   `stable` branch (`BranchModelPlugin.BranchNamespace.Root.Name`) exists and
   checks it out, then calls `RemoveRepositoryInfoAndCodeCakeBuilderAndSlnx` and
   commits `"Initialize stable branch with slnx and no RepositoryInfo.xml nor
   CodeCakeBuilder."` (a no-op commit when nothing changed).
4. **`develop` → `dev/stable` integration**: for each repo, ensures the branch
   model's dev branch (`stable.EnsureDevBranch()`), and if a `develop` branch
   exists and is ahead of `master` (`CalculateHistoryDivergence`), synchronizes
   `stable`'s dev branch and merges `develop` into it via
   `BranchLink.IntegrateMerge` (without removing `develop`). Finally checks out
   `dev/stable`.

### `RemoveRepositoryInfoAndCodeCakeBuilderAndSlnx`

The core per-repo cleanup shared by both the command and the `OnFixStart`
handler. It:

- Deletes `RepositoryInfo.xml` and `appveyor.yml`.
- If a `<RepoName>.sln` exists: runs `dotnet sln migrate` to produce the
  `.slnx`, deletes the old `.sln` on success, and runs
  `dotnet sln remove CodeCakeBuilder/CodeCakeBuilder.csproj`.
- Deletes the `CodeCakeBuilder/` folder.
- Strips `<File Path="RepositoryInfo.xml">` and
  `<File Path="Common/SharedKey.snk">` entries from the generated `.slnx`.
- If `nuget.config` exists, removes the legacy `local-feed` package source
  (initializing `<packageSourceMapping>` from the existing sources as a
  side effect of `NuGetHelper.SetOrRemoveNuGetSource`).
- Re-saves every `.csproj`, `.props` and `.targets` file under the repo
  (`XmlHelper.SafeSave`, preserving whitespace) purely to normalize them
  (no XML declaration, no BOM) — failures here are swallowed.

The three steps that touch the `.slnx` — `dotnet sln migrate`, `dotnet sln
remove` and the `.slnx` edit — go through a private `Retry`: 5 attempts,
100ms apart, the same shape `FileHelper` uses for its own deletions. Each of
them writes the file the step before it just wrote, and on Windows a process
that has exited can hold its handle a moment longer, so `dotnet sln remove`
reports *"The process cannot access the file ... because it is being used by
another process"* and a whole `ckli fix start` fails on a race that a second
attempt wins. Retrying is safe because every step is idempotent: `sln migrate`
regenerates the `.slnx`, `sln remove` answers *"could not be found"* (and `0`)
once the project is gone, and the `.slnx` edit removes elements that may
already be absent.

A retried attempt logs a **warning**, never an error: `FixStartAsync` fails the
workflow start on any error *logged* during the `OnFixStart` event, so only
giving up after the last attempt logs one.

### Notable state / behavior

- The plugin holds no mutable state of its own beyond the injected plugin
  references; all "state" is the git history and working-tree changes it
  produces in each repo.
- `MigrateNet8` is written to be re-run safely on partial failure: the
  master → stable step is explicitly idempotent, and file-removal/commit steps
  no-op when there is nothing left to change.
- Several steps hard-code legacy branch/file names (`master`, `develop`,
  `master-Net6`, `stable`, `dev/stable`) and are only meaningful for Stacks that
  actually originate from that Net8-era convention — they are not generic
  operations.
