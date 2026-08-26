# CKli.CommonFiles.Plugin

**CKli.CommonFiles.Plugin** is a `PrimaryPluginBase` plugin of [CKli](https://github.com/CK-Build/CKli) that keeps a set of
"common files" (`.editorconfig`, `.gitignore` fragments, `LICENSE`, CI snippets, etc.) synchronized across every repository of a World,
by driving them from a single shared template folder stored once in the Stack.

## Why

Multi-repository Stacks tend to accumulate small files that should be identical (or at least present) everywhere: coding style
configuration, license headers, root-level `.gitignore`, CI wiring, ... Copy-pasting them by hand drifts over time and nobody notices
until a build breaks or a review flags an inconsistency.

This plugin turns that class of problem into a single **content issue** check: it reads a folder of source-of-truth files once
(`Common/` under the World's shared data folder) and, whenever [`ckli issue`](../../CKli.Core/README.md#commands) analyzes a
repository's content, reconciles the target repository against that folder — creating missing files, fixing content or name-casing
drift, or leaving files alone, depending on how each source file opted in.

## How

### Wiring: a content-issue subscriber, not a command owner

`CommonFilesPlugin` ([`CommonFilesPlugin.cs`](CommonFilesPlugin.cs)) declares no `[CommandPath]` commands of its own. Its only job is to
subscribe to `BranchModelPlugin.ContentIssue` (raised for every active `HotBranch` during `ckli issue`) and react in
`ContentIssueRequested`:

```csharp
public CommonFilesPlugin( PrimaryPluginContext primaryContext, BranchModelPlugin branchModel )
    : base( primaryContext )
{
    _branchModel = branchModel;
    _branchModel.ContentIssue += ContentIssueRequested;
}
```

`BranchModelPlugin` is injected by constructor (the CKli.Core plugin machinery wires dependent plugins automatically), which also makes
this plugin depend on — and be disabled without — `CKli.BranchModel.Plugin` and (transitively) `CKli.ShallowSolution.Plugin`, since
`ContentIssueEventArgs.Content` reads the repository's tree through the shallow-solution file provider rather than the live working
folder.

### The `Common/` source folder and its three file kinds

The source-of-truth folder is `<World's shared data folder>/Common/` (`CommonFilesPlugin.CommonFolder`, resolved once and cached).
`ReadCommonFolder` walks it recursively; each file's *behavior* is selected by an optional bracketed marker prefixed to its file name:

| File name pattern | `FileType` | Behavior |
|---|---|---|
| `SomeFile.txt` (no marker) | `AlwaysCopy` | Created if missing; updated whenever its content differs from the source. Effectively "this file is always identical everywhere." |
| `[InitOnly] SomeFile.txt` | `InitOnly` | Created only if missing. If it already exists its content is left untouched (only a name-casing fix is applied); useful for files meant to be customized per-repository after their first creation (e.g. a `LICENSE` a repo owner may edit). |
| `[Template] SomeFile.txt` | `Template` | Not copied directly: raises the `TemplateRequired` event so another plugin can render it (e.g. substitute per-repo values) and mark it `Handled`. |

The relative target path is derived from the file's path under `Common/`, with the `[Marker]` prefix stripped and the placeholder
`$RepositoryName$` substituted with the repository's folder name (`Repo.DisplayPath.LastPart`). The legacy placeholder
`$SolutionName$` is rejected with a `CKException` telling the author to rename it to `$RepositoryName$`.

### Applying a file

For each entry, `ContentIssueRequested` dispatches on `FileType` against `ev.Content` (the target repository's content, an
`INormalizedFileProvider`) and reports through `ev.Issues` (a `ContentIssueEventArgs.Collector`, shared with `BranchModelPlugin`'s own
checks):

- `CopyFile` — `ev.Issues.CreateFile(...)` if the target is missing, or `ev.Issues.UpdateFile(...)` if the byte content differs; both
  also call `ev.Issues.CheckExistingFileCase(...)` to catch and fix name-casing mismatches.
- `InitializeFile` — creates the file only when absent; otherwise only fixes casing and logs (`Info`/`Trace`) that the file was left
  as-is.
- `HandleFileTemplate` — raises `TemplateRequired` with a `CommonFileTemplateEventArgs` carrying the original `ContentIssueEventArgs`,
  the template's `SourcePath` and its computed `RelativeTargetPath`.

### `TemplateRequired` and `CommonFileTemplateEventArgs`

[`CommonFileTemplateEventArgs`](CommonFileTemplateEventArgs.cs) is a `WorldEventArgs` exposing `ContentIssueEvent`, `SourcePath`,
`RelativeTargetPath`, and a settable `Handled` flag. Any other plugin can subscribe to
`CommonFilesPlugin.TemplateRequired` to render `[Template]`-marked files (for example, generating a `LICENSE` header with the current
year and repository name) and must set `Handled = true`; if no subscriber handles a template file, `ContentIssueRequested` raises an
`Error` on the issue's monitor, which fails the `ckli issue` command for that repository.

### Failure semantics

Because everything is reported through the shared `Collector`/monitor of the content-issue pass, any `Error` or `Fatal` log
(including the "no handler for template" case) makes `ckli issue` report a failure for the repository, and `ckli issue --fix` will
apply the queued `CreateFile`/`UpdateFile` actions to actually write the files.
