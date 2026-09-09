# CKli.Core

**CKli.Core** is the core NuGet library of [CKli](https://github.com/CK-Build/CKli), a .NET tool for managing **multi-repository stacks**.

It provides the fundamental abstractions for grouping Git repositories into named sets ("Worlds"), orchestrating cross-repository operations, and extending
behavior through a plugin system - all without requiring knowledge of any specific technology (that is the job of plugins).


## Common helpers to know

This assembly provides some general helpers that are use by all CKli ecosystem:

- The **`CKliRootEnv`** (static) is initialized once at startup. It holds `AppLocalDataPath`, `SecretsStore`, `Screen`, the current directory, and the active stack path.
- The **`CKliEnv`** is the immutable context (can be derived from the root environment). `ChangeDirectory(path)` is provided for test scenarios (and for interactive mode).
- The `IRenderable` components and the `IScreen` and `ScreenType` is composable a terminal UI abstraction. Renderables are immutable objects and can be composed to
  describe screen parts.
  - `StringScreen` (captures output as a string) and `NoScreen` (discards all output) are available for testing.
- The [`Helpers/`](Helpers) folder contains basic helpers. Plugins are invited to use them as much as possible in order to centralize behavior.


## Design Notes

- **Stateless by design.** Stack, World, and Repo objects are intended to fulfil one command and be discarded. Persistent state lives on the file system, not in memory.
- **Lazy loading with minimal caching.** Git status, plugin info, and repo layouts are computed on demand and not retained across commands.
- **Plugins are isolated.** Each plugin load uses a fresh collectible `AssemblyLoadContext`; unloading is possible without restarting the process.
- **Stable repo identity.** `CKliRepoId` (a `RandomId` stored as an annotated git tag) survives URL changes or directory moves, providing a durable cross-machine identifier for each repository.

# Core Concepts: Stack · World · Repo

## Stack

A **Stack** is a special Git repository whose name ends with `-Stack` (e.g. `CK-Build-Stack`). It is the control plane of the system: it stores World definitions and optional source-based plugins.
It is cloned once into a local folder at the root of the cloned stack folder:

| Visibility | Folder name |
|---|---|
| Public | `.PublicStack/` |
| Private | `.PrivateStack/` |

A local registry at `%LocalAppData%/CKli/StackRepositoryRegistry.v0.txt` tracks all known stacks on the machine.

A Stack repository has a single branch: `main` by convention (this is the branch that `ckli create` creates on
the remote). A Stack repository that predates this convention has a `master` branch (or any other name): CKli
then works on the repository's current branch. It never creates a purely local `main` for it — such a Stack
could never be pushed back since `PushChanges` pushes the head and the head must track a remote branch.

A Stack repository can be moved to another remote with `ckli remote stack migrate <newUrl>`: it creates the new
remote repository if needed, changes the `origin` url (`StackRepository.SetRemoteUrl`), pushes the Stack content
and archives the previous repository. The repository name must remain `{StackName}-Stack` — a migration cannot
rename a Stack — and the repositories of the Worlds are not concerned: only the Stack repository moves. The
operation is as idempotent as it can be: `SetRemoteUrl` records the previous url in the `ckli.migratedFrom` local
git configuration (`StackRepository.MigrationSourceUrl`) and the command clears it only once the previous
repository has reached its final state, so another run finishes an interrupted migration. When the hosting
provider cannot archive (the file system one cannot), this is a warning: the previous repository is left as-is.

A `StackRepository` instance is the entry point of the API. There are only 2 ways to obtain a `StackRepository`:
- Calling `TryOpenFromPath`, `OpenFromPath`, `TryOpenWorldFromPath` or `OpenWorldFromPath` from any local path.
- Calling `CloneAsync` from the remote Uri of the stack.

Once cloned, the folder contains the definition file of the default World and any number of LTS worlds, the plugins (source and
compiled form), a `$Local` and `Logs` folders.

```
StackRoot/
├── .PublicStack/           ← Stack git working directory.
│   ├── $Local/             ← Local folder (fully ignored by the .gitignore file). 
│   ├── CK-Build-Plugins/   ← Plugins solution folder.
|   |                         Contains installed plugins (NuGet CKli.XXX.Plugin package) and
|   |                         locally defined, source-based, plugins. 
│   ├── Logs/               ← Per-stack log output.
│   ├── CK-Build.xml        ← Default World definition file. Contains the repositories and the plugins configuration.
│   ├── CK-Build@net8.xml   ← A LTS World definition file.
│   └── .gitignore          ← Ignores $Local, Logs, .vs/, .idea/, and the generated CompiledPlugins.cs file.
└── ... (cloned repositories)
```

Plugins are free to add and manage any files and/or folders in `.PublicStack/` or `.PublicStack/$Local`.

## World

A **World** is a named set of repositories defined by a single XML file inside the Stack repository.
The file lists repositories, can organize them into folders and contains configurations for plugins:

```xml
<CK-Build>
  <Plugins>
    <VSSolution />
  </Plugins>

  <Repository Url="https://github.com/CK-Build/CSemVer-Net" />
  <Repository Url="https://github.com/CK-Build/SGV-Net" />
  <Folder Name="Cake">
    <Repository Url="https://github.com/CK-Build/CodeCake" />
  </Folder>
</CK-Build>
```

A Stack always has a **default World** (the current version). Long Term Support (LTS) Worlds can be derived from it (e.g. `CK-Build@net8`).
World names follow the pattern `StackName[@ltsName]`.

### `<Reference />`: the other Stacks a World uses

A world definition can name the other Stacks it works with. The elements can be direct children of the root
or be grouped in an optional `<References>` element:

```xml
<CK-Build>

  <Reference Url="https://github.com/CK-Build/CK-Build-Samples-Stack" />

  <References>
    <Reference Url="https://github.com/Invenietis/CK-Database-Stack" DefaultClone="false" />
    <Reference Url="https://github.com/Invenietis/Signature-Stack" Private="true" LTSName="@net8" />
  </References>

</CK-Build>
```

| Attribute | Default | Meaning |
|---|---|---|
| `Url` | *required* | The remote url of the referenced Stack. |
| `DefaultClone` | `true` | Whether `ckli clone` clones this reference. |
| `Private` | `false` | Whether the referenced Stack is private (a `.PrivateStack/` folder). |
| `LTSName` | *(none)* | The Long Term Support world of the referenced Stack that is used. |

`LTSName` is the only one with no default value: when it is absent, the referenced Stack's **default** world is
the one that is used. Its value must satisfy `WorldName.IsValidLTSName` (`"@net8"`) and, like the 2 booleans, an
invalid one throws when the file is loaded rather than reaching a consumer.

**A public Stack cannot reference a private one**: this is an error that prevents the world to be loaded
(the reference would be useless to anyone who can read the public Stack but not the private one).

`WorldDefinitionFile.References` exposes them as `IReadOnlyList<WorldReference>` — a read-only view of the
element with the 4 attributes above already read: `Url`, `DefaultClone`, `IsPrivate` and `LTSName`. They are
honored by the `ckli clone` command **only**: `StackRepository.CloneAsync` clones one Stack and its default
world repositories, nothing more.

Two things `WorldReference` deliberately keeps visible. `Url` is **nullable**, beside a `RawUrl` and a
`HasValidUrl`: the url is *not* validated when the world is loaded, because a hand written one may be
malformed and `ckli world reference remove` must still be able to remove such a reference — so `world
reference list` shows it in red and `ckli clone` refuses it. And `XElement` is still there, because the view
cannot express the difference between an absent attribute and one set to its default value; that difference
is what the tests of these attributes assert.

`WorldReference.Match` answers whether a reference matches a url, a repository name (`XXX-Stack`) or a stack
name (`XXX`) — case insensitively, comparing the raw url first so that a malformed one can still be named.

`WorldDefinitionFile.SetReference` and `RemoveReference` write them (and `FindReferences` resolves a url,
a repository name or a stack name to the matching references, through `Match`). Like `EnablePlugin`, they own
the whole sequence: the edit, `SaveFile` and the commit in the Stack repository. A `WorldReference` obtained
before such an edit is stale: the list is rebuilt. `SetReference` merges — a null
`defaultClone`, `isPrivate` or `ltsName` leaves the corresponding attribute as it is, and since `LTSName` has
no default value the empty string is what removes it — and it **refuses** a private
reference from a public Stack: the invariant above is enforced by an exception at load time, so writing
such a file would make it unloadable, and unfixable by `RemoveReference`. These are the two commands
`ckli world reference set` and `ckli world reference remove` (see the [command reference](../README.md#world-commands-reference-list-set-remove)).

Once a Stack is cloned, `ckli clone` reads the references of its default world and, for each of them,
clones the referenced Stack **next to** it (never inside it) or checks that it is already cloned somewhere
on this machine, then recurses into that Stack's own references. A cycle between Stacks is handled: each
url is cloned once. Two flags override the `DefaultClone` attributes:

| | |
|---|---|
| `ckli clone <url>` | Clones the references whose `DefaultClone` is not `false`. |
| `ckli clone <url> --with-ref-clone` | Clones every reference. |
| `ckli clone <url> --without-ref-clone` | Clones no reference at all. |

`LTSName` selects **which world of the referenced Stack is used**, and `ckli clone` honors it: the referenced
Stack is cloned as usual and then the repositories of that world are the ones cloned, in the world's own
`<StackRoot>/@ltsName/` folder instead of the Stack root. `StackRepository.CloneAsync` takes the `ltsName` and
resolves it through `FindWorldName`; a referenced Stack that has no such world is an **error** that fails the
whole clone. The recursion then reads the references of *that* world, not of the referenced Stack's default one.

A Stack is still cloned **once**. When two references name the same Stack with two different worlds, the
second world is *added* to that clone through the same `CKliLTSClone.AddWorld` that `ckli lts clone` uses, and
its own references are followed too. `CKliClone.CloneState` is what makes this terminate: it keys the handled
set on `(url, LTSName)` — not on the url alone — and remembers the `StackRoot` of the Stacks that this command
cloned. A Stack found already cloned *elsewhere* on the machine is never touched: that folder is not this
command's to modify, so the `lts clone` command line to run there is reported instead.

### Opening a world doesn't require its folder

`World.Create` only parses the path and reads the xml definition file, so `StackRepository.OpenWorld` can open
a world that has never been cloned — and `World.FixLayout` then clones its missing repositories. That is the
whole of `ckli lts clone`: create the folder, open the world, fix the layout. Two consequences that are easy
to miss:

- **A world's physical layout excludes the other worlds' roots.** `ReadPhysicalLayout` skips them explicitly.
  Without it the default world (rooted at the StackRoot) sees an LTS world's repositories as misplaced copies
  of its own and `layout fix` *moves* them out of it — `layout xif` adopts them, and `issue --fix` does the
  move with `deleteAliens: true`.
- **Opening a world writes into the Stack repository**: `PluginMachinery` creates its
  `{LTSName}/{StackName}-Plugins{LTSName}/` solution there, which is tracked content. `StackRepository.Close`
  only commits a dirty definition file, so whoever adds a world commits the Stack itself.
  `StackRepository.CompiledPluginsIgnorePattern` is deliberately unanchored for the same reason: the previous
  `/CKli-Plugins/CKli.Plugins/CKli.CompiledPlugins.cs` matched only the default world of a Stack literally
  named "CKli". `EnsureCompiledPluginsIgnored` repairs older Stacks when a world is added.

The `World` type is the primary type of the CKli API and the most complex one because it handles the plugins life cycle (loading, compiling, unloading).

## Repo, GitRepository & LibGit2Sharp's Repository

A [`Repo`](Repo.cs) wraps a cloned Git repository within the context of a World. It carries:

- `OriginUrl` — the remote origin URL
- `WorkingFolder` / `DisplayPath` — local paths
- `GitStatus` — cached branch name, ahead/behind counts, dirty flag
- `CKliRepoId` — a stable `RandomId` stored as an annotated git tag (`ckli-repo`), persisting identity even across URL changes

Interactions with the Git repository itself is done through the  [`GitRepository`](Git/GitRepository.cs) that itself is a
wrapper around the LibGit2Sharp's `Repository` instance.

The `GitRepository` provides numerous helpers that unifies the work with the LibGit2Sharp API (that can be complex)
and provides validations, normalizations and access control thanks to the `GitRepositoryKey` that separates read and write credentials.
Its `AccessKey` (an `IGitRepositoryAccessKey`) exposes `ToPublicAccessKey()` / `ToPrivateAccessKey()` to switch modes.

Access to private repositories (or to be able to push to public ones) uses Personal Access Tokens (PATs) resolved at runtime through `ISecretsStore`:

```csharp
public interface ISecretsStore
{
    string? TryGetRequiredSecret(IActivityMonitor monitor, IEnumerable<string> keys);
}
```

The default implementation (`DotNetUserSecretsStore`) uses the standard .NET user secrets mechanism. PAT key names follow the convention `{Prefix}_READ_PAT` / `{Prefix}_WRITE_PAT` (e.g. `GITHUB_CK-Build_READ_PAT`)
but this is eventually under control of the `GitHostingProvider`.

### Pushing: `local/` and `building/` references never reach a remote

A version tag prefixed with `building/` (a build in progress) or `local/` (a build that succeeded but is not published
yet) is a purely local artifact: another clone recomputes it, and a pushed `local/` tag actively harms the receiver (it
breaks the `fix/` branch adoption of `ckli fix start`). The same holds for any reference in those namespaces, branches
included. This is guaranteed, not merely respected by convention:

- [`GitRepository.Push`](Git/GitRepository.cs) is the single low level push: `PushBranch`, `PushTags`, the
  `DeferredPushRefSpecs` and the remote branch deletions all funnel into it. It skips — with a warning — every ref spec
  for which `IsRefusedPushRefSpec` is true: one whose destination `IsLocalOnlyRefName` (`local/` or `building/`, canonic
  or friendly name) or contains a wildcard (a wildcard cannot be proved to exclude such a reference).
- Skipping rather than failing is deliberate: a refused ref spec sitting in `DeferredPushRefSpecs` must break no
  subsequent push, and it is removed from the set so that it is not retried forever.
- Deletions (`:refs/tags/local/v1.0.0`) are never refused: a reference that reached a remote before must stay removable.
  `DeleteRemoteTags` is the only push that doesn't go through `Push` — it builds nothing but deletion ref specs.
- The commands where the user names the reference reject it up front with an error rather than a silent skip:
  `ckli tag push` and `ckli branch push`. `ckli push` names nothing (it pushes whatever tracks a remote branch), so it
  warns and skips such a branch.

---

# Git Hosting Providers

`GitHostingProvider` is the abstract base for all Git hosting integrations. It exposes a uniform API for repository lifecycle management:

```csharp
Task<HostedRepositoryInfo?> GetRepositoryInfoAsync(...)
Task<HostedRepositoryInfo?> CreateRepositoryAsync(...)
Task<bool> DeleteRepositoryAsync(...)
Task<bool> ArchiveRepositoryAsync(...)
Task<(bool Success, byte[]? Content)> GetFileContentAsync(...)
Task<string?> CreateDraftReleaseAsync(...)
Task<bool> AddReleaseAssetAsync(...)
Task<bool> FinalizeReleaseAsync(...)
```

HTTP-based providers extend `HttpGitHostingProvider`, which handles authentication, per-request `HttpClient` lifecycle, and retry hooks via `OnSendHookAsync`.

## Reading a file without cloning

`GetFileContentAsync( monitor, repoPath, filePath, refName, notFoundLogLevel, cancellation )` reads one file
out of a repository that is **not cloned** — which is the whole point: a World's `<Reference />` names another
Stack that may have no clone on this machine at all, so its `Published/index.json` has to be reached remotely.

It answers a `(bool Success, byte[]? Content)`, the same shape as `GetReleaseAsync`, because *nothing to read*
is a legitimate answer and not a failure:

| Result | Meaning |
|---|---|
| `(true, bytes)` | The file was read. |
| `(true, null)` | There is nothing to read. Logged at `notFoundLogLevel` (`Trace` by default). |
| `(false, null)` | The call failed (network, credentials, or the path names a folder). |

**`(true, null)` deliberately merges the missing file, the missing `refName` and the missing repository**: the
providers cannot all tell them apart, so relying on the distinction would work on one host and not on another.
Do not use this to probe for a repository's existence — `GetRepositoryInfoAsync` does that, and it does
distinguish.

`refName` is a branch, a tag or a commit, and defaults to null: the repository's default branch, or its `HEAD`
when `HasDefaultBranch` is false. That default costs GitLab one extra request — unlike GitHub and Gitea its
files API has no "default branch" default, so the project has to be read to learn it.

The content is `byte[]` rather than `string` because the callers parse utf-8 Json (`PublishedIndex`,
`PublishedProfile`), and a decoding step would only have to be undone.

`HostedRepositoryInfo` (sealed record) carries: `RepoPath`, `Exists`, `IsPrivate`, `IsArchived`, `Description`, `CloneUrl`, `WebUrl`, `CreatedAt`, `UpdatedAt`.

Built-in providers:

| Provider | Class | Notes |
|---|---|---|
| GitHub (cloud + enterprise) | `GitHubProvider` | |
| GitLab (cloud + self-hosted) | `GitLabProvider` | |
| Gitea | `GiteaProvider` | |
| Local filesystem | `FileSystemProvider` | For bare repos; used in tests |

---

# Plugin System

Plugins extend World behavior for specific technologies (e.g. .NET solution management, NuGet publishing). They are either:

- **Source-based** — C# projects inside the Stack repository ( the `<WorldName>-Plugins` solution), compiled on-demand into a collectible `AssemblyLoadContext`.
- **Package-based** — distributed as NuGet packages and referenced by the `<WorldName>-Plugins` solution.

A Plugin has a `PluginStatus` that can be `Available`, `DisabledByConfiguration`, `DisabledByDependency`, `DisabledByMissingConfiguration`, `MissingImplementation`.


### Plugin base classes

| Class | Role |
|---|---|
| `PluginBase` | Basic plugins receives `World` reference. Plugin types that only specialize this type are only instantiated if used by a `PrimaryPluginBase` plugin. |
| `PrimaryPluginBase` | Specialized `PluginBase` that receives a `PrimaryPluginContext`: these plugins are always instantiated. |
| `PrimaryRepoPlugin<T>` | Base type for primary plugins that create and cache per-`Repo` typed information (`T : RepoInfo`). |
| `RepoPluginBase<T>` | Base type for basic plugins that create and cache per-`Repo` typed information. |

Plugin types must specialize one of the above abstract type. They rely on each other easily: constructor injection is handled by CKli.
Even if Basic plugins are possible, most often plugins are `PrimaryPluginBase` or `PrimaryRepoPlugin<T>`. Plugins are instantiated
in the context of the World that contains them and have access to its definition (including the XML configurations, see below),
its `Repo`.

Most often they implement commands but they can also subscribe to `WorldEvents` to react to lifecycle moments:
```csharp
world.Events.FixedLayout   += e => { /* Repositories layout has been fixed: there may be new cloned repositories. */ };
world.Events.PluginInfo    += e => { /* Query the plugins. The plugins are free to react the way they want. */ };
world.Events.Issue         += e => { /* Discover (or fix) issues. */ };
```

## Plugin configuration

Each plugin has an XML element in the World definition file under `<Plugins>`:

```xml
<Plugins>
  <MyPlugin>
    <SomeOption>Value</SomeOption>
  </MyPlugin>
</Plugins>
```

This configuration element is optional and global to the World. The `PrimaryPluginContext` offers a dedicated API to read and alter
this configuration: a `PluginConfiguration` exposes plugin's `XElement`  and wraps its mutations through `Edit(monitor, editor)`.

An optional per-`Repo` configuration is also handled. The `PrimaryPluginContext` also offers a `GetConfigurationFor(repo)` / `HasConfigurationFor(repo)`.

## Plugin discovery and loading

The [`PluginMachinery`](Plugin/Impl/PluginMachinery.cs) orchestrates:
1. Discovery of plugin projects in `<WorldName>-Plugins/` inside the Stack.
2. Code generation and Compilation via `IPluginFactory` (reflection-based `None` mode, or `Debug`/`Release` compiled code generation).
3. Loading into a collectible `AssemblyLoadContext` (via `CKli.Loader`).

There's nothing simple here. An important part of the magics lies in the the **CKli.Loader** and the **CKli.Plugins.Core** assemblies and
how they are used by the `<WorldName>-Plugins` solution.

# Commands

The static `CKliCommands` (in the `CKli` namespace) registers all intrinsic commands and is the CKli entry point: it executes the `Command` (abstract)
that describes and implements a CKli command:

```csharp
public abstract class Command
{
    protected Command( string commandPath, string description, ... ) { ... }

    public string CommandPath { get; }
    public string Description { get; }
    // Arguments, Options, Flags, InteractiveMode...
    internal protected abstract ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                      CKliEnv context,
                                                                      CommandLineArguments arguments,
                                                                      CancellationToken scopeAlive );
}
```
`CommandPath` and `Description` are set once through the constructor rather than being abstract members, and `HandleCommandAsync` also receives a `CancellationToken`
that tracks the current interruptible scope (see `InterruptibleScope`). This abstract class is only used from inside CKli.Core to implement the intrinsic commands. Non intrinsic commands are implemented by Plugins as
`[CommandPath( "..." )]` decorated methods and the `Command` instance is either dynamic (when reflection is used) or is code generated as
an adapter on the command method.

- The `CKliEnv` is a immutable command context that gives access to the "current" directory to consider and the screen (for display).
- The `CommandLineArguments` parses and consumes tokens: `EatArgument()`, `EatFlag(name)`, `Close(monitor)`. It detects remaining (non consumed) tokens.

## Plugin commands

Commands are implemented by Plugins public methods decorated with a `[CommandPath( "..." )]` attribute. These methods must:
- Return a success/failure flag synchronously (`bool`) or not (`ValueTask<bool>` or `Task<bool>`).
- Their first parameter must be a `IActivityMonitor`.
- They may have a `CKliEnv` parameter. They often has in order to display text on the screen.
- They can have a `CommandLineArguments` parameter. If this is the case, there should not be any other parameters: the method must fully handle the arguments.
- If there is no `CommandLineArguments` parameter, then the following kind of parameters can appear, in this order:
  - The required parameters:
    -  `string required1, string required2, ...`
  - The optional parameters (options that must be specified behind their `--option-name` argument):
    - `string[] multiple1, string? singleOpt1 = null, string[] multipleOptions2, string? option2 = null`
  - The flags. They are all optional and default to false:
      - `bool withFlag1, bool withFlag2 = false`

CKli doesn't try to be too clever here: only strings and Booleans are handled, it is up to the commands to parse, interpret and validate
them. This approach homogenizes the command syntax (flags are always "positive"), provides an efficient way to generate `Command` adapters
and allows command implementation to be as complex as required regarding parameters handling.

A `[Description( "..." )]` attribute can decorate the method and the parameters.
A `[OptionName("<names>")]` can override the snake-case parameter name computed by default and add short forms (see below: `[OptionName("--dry-run, -d")]`).

These two examples are taken from actual Plugins in this ecosystem (`CKli.BranchModel.Plugin` and `CKli.Build.Plugin`):

```csharp
[Description( "Switch the working folder to the given branch." )]
[CommandPath( "branch switch" )]
public bool BranchSwitch( IActivityMonitor monitor,
                          CKliEnv context,
                          [Description( "Branch name to checkout." )]
                          string branch,
                          [Description( "Create and synchronize the branch if it doesn't exist, instead of switching to the closest existing one." )]
                          [OptionName("--create,-c")]
                          bool create = false,
                          [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                          bool all = false )
{
    // ...
}

[Description( "Build-Test-Package the consumers of the current repositories, propagates packages to their consumers and publishes all the artifacts." )]
[CommandPath( "*publish" )]
public Task<bool> StarPublishAsync( IActivityMonitor monitor,
                                CKliEnv context,
                                [Description( "Specify the branch to consider. By default, the current head is considered when in a Repo." )]
                                [OptionName( "--branch,-b" )]
                                string? branch = null,
                                [Description( "Maximal Degree of Parallelism. Defaults to 4." )]
                                string? maxDop = null,
                                [Description( "Consider CI builds (prerelease tags) as buildable versions." )]
                                [OptionName( "--ci" )]
                                bool ci = false,
                                [Description( "Force a new CI build even if the last one already covers the current commit." )]
                                bool ciForce = false,
                                [Description( "Skip running tests altogether." )]
                                bool skipTests = false,
                                [Description( "Run tests even if they have already run successfully on the commit." )]
                                bool forceTests = false,
                                [Description( "Only display the build roadmap." )]
                                [OptionName("--dry-run, -d")]
                                bool dryRun = false,
                                bool all = false )
{
    // ...
}
```


