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

A `StackRepository` instance is the entry point of the API. There are only 2 ways to obtain a `StackRepository`:
- Calling `TryOpenFromPath`, `OpenFromPath`, `TryOpenWorldFromPath` or `OpenWorldFromPath` from any local path.
- Calling `Clone` from the remote Uri of the stack.

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
│   └── .gitignore          ← Ignores $Local and Logs folders.
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

A Stack always has a **default World** (the current version). Long Term Support (LTS) Worlds can be derived from it (e.g. `CK-Build@net8`). World names follow the pattern `StackName[@ltsName]`.

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
and provides validations, normalizations and access control thanks to the `GitRepositoryKey` that separates read and write credentials
(with `ToPublicAccessKey()` / `ToPrivateAccessKey()` to switch modes).

Access to private repositories (or to be able to push to public ones) uses Personal Access Tokens (PATs) resolved at runtime through `ISecretsStore`:

```csharp
public interface ISecretsStore
{
    string? TryGetRequiredSecret(IActivityMonitor monitor, string[] keys);
}
```

The default implementation (`DotNetUserSecretsStore`) uses the standard .NET user secrets mechanism. PAT key names follow the convention `PREFIX_<org>_PAT` (e.g. `GITHUB_CK_BUILD_PAT`)
but this is eventually under control of the `GitHostingProvider`.

---

# Git Hosting Providers

`GitHostingProvider` is the abstract base for all Git hosting integrations. It exposes a uniform API for repository lifecycle management:

```csharp
Task<HostedRepositoryInfo?> GetRepositoryInfoAsync(...)
Task<HostedRepositoryInfo?> CreateRepositoryAsync(...)
Task<bool> DeleteRepositoryAsync(...)
Task<bool> ArchiveRepositoryAsync(...)
Task<string?> CreateDraftReleaseAsync(...)
Task AddReleaseAssetAsync(...)
Task FinalizeReleaseAsync(...)
```

Built-in providers:

| Provider | Class | Notes |
|---|---|---|
| GitHub (cloud + enterprise) | `GitHubProvider` | |
| GitLab (cloud + self-hosted) | `GitLabProvider` | |
| Gitea | `GiteaProvider` | |
| Local filesystem | `FileSystemProvider` | For bare repos; used in tests |

HTTP-based providers extend `HttpGitHostingProvider`, which handles authentication, per-request `HttpClient` lifecycle, and retry hooks via `OnSendHookAsync`.

`HostedRepositoryInfo` (sealed record) carries: `RepoPath`, `Exists`, `IsPrivate`, `IsArchived`, `Description`, `CloneUrl`, `WebUrl`, `CreatedAt`, `UpdatedAt`.

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
| `PrimaryPluginBase` | Specialized `PluginBale` that receives a `PrimaryPluginContext`: these plugins are always instantiated. |
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
    public abstract string CommandPath { get; }
    public abstract string Description { get; }
    // Arguments, Options, Flags, InteractiveMode...
    public abstract Task<bool> HandleCommandAsync(IActivityMonitor, CKliEnv, CommandLineArguments);
}
```
This abstract class is only used from inside CKli.Core to implement the intrinsic commands. Non intrinsic commands are implemented by Plugins as
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

```csharp
[Description( "Switch the working folder to the given branch." )]
[CommandPath( "checkout" )]
public bool Checkout( IActivityMonitor monitor,
                      CKliEnv context,
                      [Description( "Branch name to checkout." )]
                      string branchName,
                      [Description( "Creates the branch if it doesn't exist." )]
                      bool create = false,
                      [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                      bool all = false )
{
    // ...
}

[Description( "Build-Test-Package the consumers of the current repositories, propagates packages to their consumers and publishes all the artifacts." )]
[CommandPath( "*publish" )]
public Task<bool> StarPublish( IActivityMonitor monitor,
                                CKliEnv context,
                                [Description( "Specify the branch to consider. By default, the current head is considered when in a Repo." )]
                                [OptionName( "--branch,-b" )]
                                string? branch = null,
                                [Description( "Maximal Degree of Parallelism. Defaults to 4." )]
                                string? maxDop = null,
                                [Description( "Publish all the Repos, not only the ones that consume or produce the current repositories." )]
                                bool all = false,
                                [Description( "Run tests even if they have already run successfully on the commit." )]
                                bool forceTests = false,
                                [Description( "Don't publish the generated packages and asset files." )]
                                [OptionName("--no-publish")]
                                bool noPublish = false,
                                [Description( "Only display the build roadmap." )]
                                [OptionName("--dry-run, -d")]
                                bool dryRun = false )
{
    // ...
}
```


