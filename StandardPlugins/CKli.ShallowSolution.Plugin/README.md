# CKli.ShallowSolution.Plugin

**CKli.ShallowSolution.Plugin** gives other CKli plugins a cheap, read-only model of a .NET repository's solution:
which `.csproj` projects it contains and which NuGet packages they consume — for *any* commit or branch, not just
whatever happens to be checked out on disk.

It exposes **no `[CommandPath]` commands** and **subscribes to no `World.Events`**. It is a foundation library plugin:
a `PrimaryPluginBase` that other primary plugins take as a constructor parameter and call directly. Despite what the
name might suggest, it does **not** generate or maintain a repo-spanning aggregate solution (nothing like the
`CKli-Plugins.slnx` that the plugin-collection machinery maintains in a Stack's `*-Plugins` folder) — every operation
here is scoped to a single `Repo`'s own solution file.

## Why

Reasoning about ".NET repository content" — what projects exist, what they depend on, whether a package needs
bumping — is something several other plugins need repeatedly and at scale:

- **`CKli.BranchModel.Plugin`** reads a repo's solution on every relevant branch to detect content issues
  (`ContentIssueBuilder`/`ContentIssueEventArgs` call `TryGetShallowSolution` for the "GitContent" branch).
- **`CKli.HotZone.Plugin`** (via `CKli.VersionTag.Plugin` → `CKli.ArtifactHandler.Plugin` → `CKli.BranchModel.Plugin`)
  builds its dependency graph (`HotGraph`) by calling `GetShallowSolution` for each repo/branch pair it walks.
- **`CKli.Build.Plugin`** (which depends on `CKli.HotZone.Plugin`) uses the same solutions to compute build/publish
  roadmaps and version bumps.

Doing this properly — resolving `TargetFramework`s, transitive references, lock files, MSBuild imports — needs real
tooling (MSBuild evaluation, [Buildalyzer](https://github.com/daveaglick/Buildalyzer), or an actual `dotnet
restore`/build). `ShallowSolutionPlugin`'s own doc comment calls its approach "simple (even brutal), but it covers
our current needs": it only reads the `<Project Path="..." />` entries of a `.slnx` file, the `.csproj` files they
point to, and any `Directory.Build.props` / `Directory.Packages.props` reachable from each project up to the solution
root. No property expansion, no dependency resolution, no MSBuild involved — just enough XML to know **which
projects exist**, **whether each is packable**, and **which packages (`PackageReference` / `PackageVersion`) are
consumed**, at whatever version is literally written in the file.

The payoff of that trade-off: a solution can be analyzed straight out of the Git object database, from a `Commit`
that was never checked out, and many repos/commits can be batch-analyzed quickly (e.g. while computing a `HotGraph`
or a publish roadmap) without ever running `dotnet restore`.

### Convention

A repo's solution file must be a `.slnx` (the newer XML solution format) named after the repository itself: for a
repo displayed as `CKli`, the plugin expects `CKli.slnx` at the repository root
(`Repo.DisplayPath.LastPart + ".slnx"`). Read paths that require the file fail loudly when it's missing; the mutable
path (`MutableSolution`) is more forgiving and can migrate/rename an existing `.sln` into that convention (see
below).

## How

### `ShallowSolutionPlugin` — the entry point

```csharp
public sealed class ShallowSolutionPlugin : PrimaryPluginBase
{
    public ShallowSolutionPlugin( PrimaryPluginContext primaryContext );
    // ...
}
```

Being a `PrimaryPluginBase`, it is always instantiated once per `World` (it reads no `<ShallowSolution>` configuration
element — `PrimaryPluginContext.Configuration` is never touched) and is injected by constructor into whichever other
primary plugin needs it, e.g. `BranchModelPlugin( ..., ShallowSolutionPlugin shallowSolution )`.

| Member | Purpose |
|---|---|
| `GetShallowSolution(monitor, repo, branch, useWorkingFolder)` | Loads the `GitSolution` for a branch tip. The `.slnx` file is **required**; an error is logged and `null` returned if missing. Throws if `branch.IsRemote`. |
| `TryGetShallowSolution(monitor, repo, branch, useWorkingFolder, out solution)` | Same, but the `.slnx` file is **optional**: returns `true` with `solution == null` when absent (not an error), `false` only on an actual failure. |
| `GetRequiredContent(monitor, repo, commit, useWorkingFolder)` | Same idea for an arbitrary `Commit` (not necessarily a branch tip); returns a plain `GitSolutionContent` (no `Repo`/`Branch` attached). |
| `GetFiles(commit, useWorkingFolder)` / `GetFiles(tree)` | Returns an `INormalizedFileProvider` over a commit's/tree's content. Results are cached in a private `Dictionary<string, TreeFolder>` keyed by `Tree.Sha`, so re-analyzing the same commit is cheap. |
| `UpdatePackages(monitor, repo, mapping, updated)` | Convenience wrapper: `MutableSolution.Create(monitor, repo)` then `MutableSolution.UpdatePackages`, for the **currently checked-out** working folder. |

The `useWorkingFolder` flag on every read method controls the file source: when `true` **and** the requested commit
is the branch/repo `HEAD`, files are read straight from the physical file system (`CheckedOutFileProvider`);
otherwise they always come from the Git object database via LibGit2Sharp (`TreeFolder`), regardless of what is
currently checked out. The XML doc is explicit about the danger of misusing this: a working-folder provider "must not
be used anymore" once another commit gets checked out — "or kittens will die" — because it has no cache and no
tracking.

### Uniform file access: `INormalizedFileProvider`

```csharp
public interface INormalizedFileProvider
{
    IDirectoryContents? GetDirectoryContents( NormalizedPath sub );
    IFileInfo? GetFileInfo( NormalizedPath sub );
}
```

A trimmed-down `Microsoft.Extensions.FileProviders.IFileProvider`: it uses CK.Core's `NormalizedPath`, drops the
`Watch` API, and returns `null` instead of the usual "not found" sentinel objects. Two internal implementations
(`GitFile/` folder):

- **`TreeFolder`** wraps a LibGit2Sharp `Tree`, so a commit that was never checked out can still be browsed like a
  file system. It compensates for a `.git/config ignorecase` quirk (a case-insensitive lookup, e.g. `nuget.config` vs
  `NuGet.config`, can silently fail against `Tree` indexers) by falling back to a linear case-insensitive scan.
- **`CheckedOutFileProvider`** wraps a `PhysicalFileProvider` over the repo's working directory, with the same
  case-matching care so callers see the on-disk casing regardless of the casing they queried with.
- **`GitFileInfo`** adapts a LibGit2Sharp `TreeEntry` to `IFileInfo`/`IDirectoryContents` so both providers expose the
  identical `Microsoft.Extensions.FileProviders` shape.
- **`FileInfoExtensions`** adds `ReadAsText()` / `ReadAsBytes()` helpers on top of `IFileInfo`.

### Parsing projects and packages: `CommonSolution`, `GitSolutionContent`, `GitSolution`

`CommonSolution.LoadAllProjectFiles` (internal `static`, shared by both the read-only and the mutable paths) is the
actual solution walker:

1. For every `<Project Path="..." />` under the `<Solution>` root, resolves `Path` through the
   `INormalizedFileProvider`, loads it as XML, and hands `(path, XElement)` to a `collector` callback.
2. For every distinct folder between that project and the solution root (deduplicated via a `HashSet<string>` so a
   shared parent is only visited once), also loads — if present — `Directory.Packages.props` then
   `Directory.Build.props`, walking up folder by folder, feeding each to the same `collector`.
3. Missing `<Project Path="...">` targets are only warned about; anything else failing to parse aborts the whole walk
   (`false`).

`GitSolutionContent` turns that walk into a read-only model via its `collector`:

| Member | Meaning |
|---|---|
| `Projects` (`IReadOnlyList<Project>`) | One entry per `.csproj`. `Project.Path`, `Project.Name` (file name without `.csproj`), and `Project.IsPackable` (lazily read from `<PropertyGroup><IsPackable>`, `null` if absent). |
| `Consumed` (`IReadOnlySet<PackageInstance>`) | Every package id + `SVersion` found across `<PackageReference>` (in `.csproj` and `Directory.Build.props`) and `<PackageVersion>` (in `Directory.Packages.props`, i.e. NuGet Central Package Management). A `<PackageReference VersionOverride="...">` wins over its own `Version`; a `<PackageReference>` with neither attribute contributes nothing (its version is assumed centrally managed). **One version per package identifier** — see below. |
| `HasUpdates(mapping)` | Whether any consumed package would change version under a single `IPackageMapping`. |
| `HasUpdates(mapping, ref updates)` | Same, and collects the diffs into a `PackageMapper`. |
| `HasUpdates(action, mappings...)` | Checks several mappings in priority order per package, invoking `action(package, newVersion, mappingIndex)` for each match. |

#### One version per package identifier, per repository

A repository may reference a package identifier in **one version only**. `GitSolutionContent.AddConsumed` enforces
it while collecting, and a violation is an **error**: the solution does not load at all, so every command that needs
it fails with a message naming the identifier, both versions and both files.

This is decided on the *shallow* read, which ignores every `Condition` — so two conditional
`<PackageReference>` across target frameworks are two versions and are refused like any other pair. That is
deliberate: a single version per identifier is what the whole dependency model is made of. The mapping that aligns
and rewrites references (`IPackageMapping`, and `Roadmap.PackageMapping` above it) maps a package identifier to a
single version and structurally *cannot* express two, and a produced package carries one version for each of its
dependencies.

Note what is **not** covered by this rule:

- **Discrepancies between repositories are expected and are not an error.** They are transient and healed by the
  build: `HotGraph.PackageUpdater.Discrepancies` collects them across solutions and `DiscrepanciesMapping` maps each
  to the greatest referenced version, which `MutableSolution.UpdatePackages` then writes. Only the repository-local
  case is refused, and precisely *because* nothing can heal it: aligning it would silently rewrite one of the two
  references and change what the developer wrote.
- **A `VersionOverride` is exempt.** Under Central Package Management it exists precisely to differ from the declared
  `<PackageVersion>`, and it is the only legal way to say so (a `<PackageReference Version="...">` alongside a
  `<PackageVersion>` is the NuGet error NU1008). It is not recorded as the first version met either, so a later
  regular reference is compared against the central declaration rather than against the override.

`GitSolution : GitSolutionContent` adds nothing but identity: the `Repo` and `Branch` it was read from (used by
callers, e.g. `HotZone.Plugin`, that need to report *where* a project/package came from). It is only constructible
through `ShallowSolutionPlugin.GetShallowSolution`/`TryGetShallowSolution`.

### Mutating solutions on disk: `MutableSolution`

`MutableSolution` is the only writable side of the plugin, and it only ever touches the **physical working folder**
of an already-checked-out `Repo` — never Git history.

`MutableSolution.Create(monitor, repo)` first locates the solution file through `FindSolutionFile`, which tolerates a
messier reality than the strict read path:

1. If the conventional `<RepoName>.slnx` exists, use it (warn if the file system entry's casing differs).
2. Else, if a `.sln` file exactly named `<RepoName>.sln` exists, it's a legacy solution: migrate it in place with
   `dotnet sln "<file>" migrate` (via `ProcessRunner.RunProcess`), delete the old `.sln`, and treat the resulting
   `.slnx` as authoritative.
3. Else, if exactly one `.slnx` (or, failing that, exactly one `.sln`) file of *any* name exists at the repo root,
   rename it to the conventional name.
4. Any other situation — no candidate, or more than one `.sln`/`.slnx` candidate, or both an exactly-named `.sln` and
   an unrelated `.slnx` present at once — is reported as an error and the whole operation aborts.

Once loaded, `UpdatePackages(monitor, mapping, updated)` is the only mutation supported: it re-walks every project
and `Directory.(Build|Packages).props` file via `CommonSolution.LoadAllProjectFiles` (this time with
`LoadOptions.PreserveWhitespace`, to keep diffs minimal), rewrites each `<PackageReference>`'s `VersionOverride`
and/or `Version` attribute (and each `<PackageVersion>`'s `Version` attribute) using
`IPackageMapping.TryGetMappedVersion`, and saves every touched document back with `XmlHelper.SafeSave`. Packages with
no matching mapping are left untouched; packages that *are* mapped but whose current version isn't recognized by the
mapping produce a warning, not an error. The method's doc comment explains the motivation: `dotnet package update`
only operates on one project at a time and silently no-ops when a package isn't found — unusable for a batched,
multi-repository update.

### Package mappings: `IPackageMapping`, `PackageMapper`, `BrutalPackageMapper`

```csharp
public interface IPackageMapping
{
    bool IsEmpty { get; }
    bool HasMapping( string packageId );
    SVersion? GetMappedVersion( string packageId, SVersion from );
}
```

`PackageMappingExtensions.TryGetMappedVersion` adapts `GetMappedVersion` to the usual `bool` + `out` pattern. Two
implementations are provided:

- **`PackageMapper`** — the precise mapper. Each `(packageId, fromVersion)` pair maps to an explicit `toVersion`
  (`TryAdd`/`Add`; idempotent, conflicts on a differing target for the same `(id, from)`), so different current
  versions of the same package can map differently, or not map at all. It implements
  `ICKVersionedBinarySerializable` (`[SerializationVersion(0)]`), so a computed mapping — e.g. one produced while
  walking a Build roadmap — can be persisted or transmitted. `PackageMapper.Empty` is a shared no-op instance.
- **`BrutalPackageMapper.Create(mappings)`** — wraps a plain `IReadOnlyDictionary<string, SVersion>` (package id →
  target version; must use `StringComparer.OrdinalIgnoreCase`, checked at construction) and ignores the *current*
  version entirely: any known package id is always mapped to its target version.

## Key types at a glance

| Type | Role |
|---|---|
| `ShallowSolutionPlugin` | `PrimaryPluginBase` entry point; reads solutions from commits/branches (cached per `Tree.Sha`), dispatches to `MutableSolution` for updates. |
| `GitSolutionContent` / `GitSolutionContent.Project` | Read-only projects + consumed packages, independent of any `Repo`/`Branch`. |
| `GitSolution` | `GitSolutionContent` bound to the `Repo`/`Branch` it was read from. |
| `MutableSolution` | Working-folder-only solution used to rewrite package versions in place; handles `.sln` → `.slnx` migration and renaming. |
| `CommonSolution` | Internal shared walker: resolves `.slnx` `<Project>` entries and `Directory.*.props` files. |
| `INormalizedFileProvider` / `TreeFolder` / `CheckedOutFileProvider` / `GitFileInfo` / `FileInfoExtensions` | Uniform read-only file access over either a Git `Tree` or the physical working folder. |
| `IPackageMapping` / `PackageMappingExtensions` / `PackageMapper` / `BrutalPackageMapper` | Package-id + version → target-version mapping abstraction used for update detection and application. |

## Configuration

None. The plugin never reads `PrimaryPluginContext.Configuration` or any per-`Repo` configuration — there is no
`<ShallowSolution>` element to declare under a World's `<Plugins>`. Its only "configuration" is the implicit
`<RepoName>.slnx` file-naming convention described above.

## Commands and events

None. `ShallowSolutionPlugin` declares no `[CommandPath]` methods and does not subscribe to any `World.Events`
(`FixedLayout`, `PluginInfo`, `Issue`). It is called synchronously, in-process, by whichever plugin holds a reference
to it — it never regenerates or rewrites anything on its own initiative.

## Dependencies

The project only references `CKli.Core` (for `Repo`, `NormalizedPath`, `XmlHelper`, `ProcessRunner`, `SVersion`,
etc.); LibGit2Sharp and `Microsoft.Extensions.FileProviders` come in transitively through it.

```xml
<ItemGroup>
  <ProjectReference Include="..\..\CKli.Core\CKli.Core.csproj" />
</ItemGroup>
```

It is consumed directly (`ProjectReference`) by `CKli.BranchModel.Plugin`, and transitively by everything downstream
of it:

```
CKli.ShallowSolution.Plugin
  └─ CKli.BranchModel.Plugin
       └─ CKli.ArtifactHandler.Plugin
            └─ CKli.VersionTag.Plugin
                 └─ CKli.HotZone.Plugin
                      └─ CKli.Build.Plugin
```
