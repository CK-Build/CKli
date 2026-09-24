# CKli

CKli is a tool for <u>multi-repositories</u> stacks.
It allows to automate actions (build, package upgrade, etc...), on <u>Worlds</u> (a group of repositories),
and concentrates information in a single place.

> CKli is implemented in .Net but is not limited to .Net solutions and projects.
>
> The core commands can handle any git repositories regardless of their content.
> Only the Standard Plugins (see below) are dedicated to .Net development. 

## Getting Started

### Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download)

### Installation

CKli is a [dotnet tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools).
You should install it globally by running:

```powershell
dotnet tool install CKli -g
```
And auto-update it with:

```powershell
ckli update
```
The `update` command handles switching from production and pre-releases easily (described below).

Stable versions are on [nuget.org](https://www.nuget.org/packages/CKli); the CI builds are on the
Signature-OpenSource feed. To manually install a specific version (replace `<version>` with the one you want):
```powershell
dotnet tool update CKli@<version> -g --prerelease --add-source https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json --no-http-cache
```

### Run CKli

If you installed CKli globally, you can run `ckli` in any command prompt to start it.

`ckli --help` lists the commands and namespaces; `ckli <command> --help` details one of them
(`--help` must be the last argument). [The commands](#the-commands) below is the whole map, with
where each one is documented.

A few options are global: they belong to `ckli` itself rather than to a command, and
`ckli --help --global` details them.

- `--path <dir>` (or `-p`) sets the working path instead of the current directory. It must come first
  (before the command): after the command, `--path` is an option of that command.
- `--version` (or `-v`) displays the installed CKli version. It must come first and excludes anything else.
- `--ckli-screen <none|no-color|force-ansi>` changes the display. A non empty `NO_COLOR` environment
  variable is honored (see <https://no-color.org/>).
- `--ckli-debug` launches a debugger when starting.

`ckli i` (or `ckli interactive`) starts an interactive loop where commands are typed one after the other
without the `ckli` prefix. It accepts `--path` before or right after it. The commands that create or
relocate something are rejected in interactive mode: `clone`, `create`, `world lts clone`, `world lts create`,
`remote stack migrate` and `update`.

## The basics: Stack-World-Repo

A World is a set of Git repositories. The set is described by a simple XML file that lists the
repositories, organizes them in a folder structure and provides plugin configurations.
Below is the CKli default world definition file (there is no folder structure here):
```xml
<CKli MinCKliVersion="0.14.0">
  <Plugins CompileMode="Debug">
    <ArtifactHandler>
      <NuGet>
        <Feed Name="NuGet"
              Url="https://api.nuget.org/v3/index.json"
              PushQualityFilter="[papa,]">
          <Credentials SecretKey="NUGET_ORG_PUSH_API_KEY" />
        </Feed>
        <Feed Name="Signature-OpenSource"
              Url="https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json"
              PushQualityFilter="[,],AllowCI">
          <Credentials SecretKey="AZURE_FEED_SIGNATURE_OPENSOURCE_PAT" />
        </Feed>
      </NuGet>
    </ArtifactHandler>
    <BranchModel />
    <Build />
    <CommonFiles />
    <HotZone />
    <Migration />
    <Publish />
    <ShallowSolution />
    <VersionTag />
  </Plugins>
  <Repository Url="https://github.com/CK-Build/CKli" />
  <Repository Url="https://github.com/CK-Build/CK-SVersion" />
  <Repository Url="https://github.com/CK-Build/CK-Packaging-Abstractions" />
</CKli>
```
This definition file is stored in the `main` branch of a Stack repository — a Stack repository has a single
branch and it must be `main` (a repository that hasn't got it is refused, not adapted to):

```
ckli clone https://github.com/CK-Build/CKli-Stack
```

A Stack contain at least one World: the default World that is the _current version of the Stack_.
Long Time Support (LTS) Worlds can be created any time from the default World.

The `<Plugins />` element above enables the 9 Standard Plugins. They are optional and each one is
documented on its own:

| Plugin | What it does |
|---|---|
| [`ArtifactHandler`](StandardPlugins/CKli.ArtifactHandler.Plugin/README.md) | The NuGet feeds a World reads and pushes to, and the `nuget.config` of every Repo. |
| [`BranchModel`](StandardPlugins/CKli.BranchModel.Plugin/README.md) | The branch structure (`stable`, the prerelease chain, `dev/`, `explo/`) and the `branch` commands. |
| [`Build`](StandardPlugins/CKli.Build.Plugin/README.md) | The `build`, `publish`, `fix` and `deps update` commands: the roadmap and what it rebuilds. |
| [`CommonFiles`](StandardPlugins/CKli.CommonFiles.Plugin/README.md) | The files that must be identical (or reconciled) across the Repos. |
| [`HotZone`](StandardPlugins/CKli.HotZone.Plugin/README.md) | The dependency graph of the World and the version zone each Repo is in. |
| [`Migration`](StandardPlugins/CKli.Migration.Plugin/README.md) | One-off repository conversions (Net8 → Net10). Transient by design. |
| [`Publish`](StandardPlugins/CKli.Publish.Plugin/README.md) | Pushing the artifacts, the releases, and the `Published/` profiles a World leaves behind. |
| [`ShallowSolution`](StandardPlugins/CKli.ShallowSolution.Plugin/README.md) | Reading a solution and its package references straight from a branch, without MSBuild. |
| [`VersionTag`](StandardPlugins/CKli.VersionTag.Plugin/README.md) | The version tags (`vX.Y.Z`, `local/`, `building/`, `+fake`, `+deprecated`) and their invariants. |

Two workflows span several of them: [the branch model and the hot zone](StandardPlugins/HotZone-Workflow.md)
and [the fix workflow](StandardPlugins/Fix-Workflow.md).

## Private & Public stack and repositories

A Stack is public or private **as a whole**: there is no per-repository setting and every Repo of a World
inherits the flag of its Stack. The Stack folder name is where it is recorded — `.PublicStack/` or
`.PrivateStack/` — and it is chosen once, by the `--private` flag of
[`clone`](CKli.Core/CKliCommands/README.md#clone-url---lts-name-ltsname---max-dop-n---private---allow-duplicate---ignore-parent-stack---with-ref-clone---without-ref-clone)
or [`create`](CKli.Core/CKliCommands/README.md#create-url---private---ignore-parent-stack).

What the flag actually changes is what needs a secret:

|  | Read (clone, fetch, pull) | Write (push, create a remote repository, publish a release) |
|---|---|---|
| Public Stack | no secret at all | a secret is required |
| Private Stack | a secret is required | a secret is required |

So a **write secret is always required**, even for a public Stack: only anonymous reading is free.

A Stack can reference another Stack that has its own flag: see `<Reference Private="true" />` in the
[`clone`](CKli.Core/CKliCommands/README.md#clone-url---lts-name-ltsname---max-dop-n---private---allow-duplicate---ignore-parent-stack---with-ref-clone---without-ref-clone)
and [`world reference set`](CKli.Core/CKliCommands/README.md#world-reference-set-stackurlorname---lts-name-ltsname---default-world---no-default-clone---default-clone---private---public---allow-lts)
sections. A public Stack cannot reference a private one.

A World definition file only ever holds the **name** of a key, never a secret value — and you never have to
work that name out yourself. It is computed from the remote url, and whenever an operation needs a secret
that is not registered, CKli stops and logs the exact `dotnet user-secrets set ...` command line to run.
The whole scheme (the secret store, the `{Prefix}_READ_PAT` / `{Prefix}_WRITE_PAT` derivation, and what
each command actually needs from a token) is documented with its implementation, in
[`CKli.Core`'s README](CKli.Core/README.md#secrets-keys-and-pats).

## The commands

There are more than 50 of them, but `ckli --help` is a map rather than a list: one line per depth 1 command
or namespace with a one line summary, and the child names right below a namespace. `ckli <command> --help`
then details one of them in full — `ckli tag --help` for a whole namespace, `ckli tag push --help` for a
single command. (Why the help collapses, and where each summary comes from, is in
[`CKli.Core`'s README](CKli.Core/README.md#the-two-help-modes).)

The table below is that same map, with where each entry is documented.

**The intrinsic commands** are the ones CKli itself implements. They handle Stack, World, Repo and Git and
know nothing of what a repository *contains*, so they work on any Git repository. Their reference lives
next to their implementation, in
[`CKli.Core/CKliCommands/README.md`](CKli.Core/CKliCommands/README.md).

**Every other command comes from a plugin** and is documented with that plugin: a World that doesn't enable
the plugin simply doesn't have the command. What follows is what the 9 Standard Plugins add — another
plugin adds its own, and its commands appear in `ckli --help` the same way.

| Command | What it does | Reference |
|---|---|---|
| **`branch`**<br/>`close` `list` `open` `push` `switch` `sync` | Git branch operations applied to the current Repo or to all the Repos of the World. Opens, closes, switches and synchronizes the branches of the CKli branch model. | [`BranchModel`](StandardPlugins/CKli.BranchModel.Plugin/README.md#commands) — except [`branch push`](CKli.Core/CKliCommands/README.md#branch-push-branch---all), which is intrinsic |
| **`build`** | Build-Test-Package, keeping the produced packages local. | [`Build`](StandardPlugins/CKli.Build.Plugin/README.md#commands) |
| **`*build`** | Upstream closure build: builds the producers of the current repositories. | [`Build`](StandardPlugins/CKli.Build.Plugin/README.md#commands) |
| **`clone <stackUrl>`** | Clones a Stack and the repositories of one of its Worlds in the current directory. | [intrinsic](CKli.Core/CKliCommands/README.md#clone-url---lts-name-ltsname---max-dop-n---private---allow-duplicate---ignore-parent-stack---with-ref-clone---without-ref-clone) |
| **`commit <message>`** | Commit any pending changes. Does nothing if there's no change to commit. | [`BranchModel`](StandardPlugins/CKli.BranchModel.Plugin/README.md#commands) |
| **`create <stackUrl>`** | Creates a new Stack and its remote repository in the current directory. | [intrinsic](CKli.Core/CKliCommands/README.md#create-url---private---ignore-parent-stack) |
| **`deps`**<br/>`update` | Aligns the external dependencies of the World. | [`Build`](StandardPlugins/CKli.Build.Plugin/README.md#deps-update-aligning-the-external-dependencies) |
| **`exec <process-name-and-args>`** | Executes an external process on each Repo. | [intrinsic](CKli.Core/CKliCommands/README.md#exec----ckli-continue-on-error---ckli-all) |
| **`fetch`** | Fetches all branches (and optionally tags) from the remote(s). | [intrinsic](CKli.Core/CKliCommands/README.md#fetch---all---with-tags---max-dop-n) |
| **`fix`**<br/>`build` `cancel` `info` `publish` `push` `start` | Builds and publishes a Fix Workflow. Starts, shares, inspects and cancels a Fix Workflow. | [the Fix Workflow](StandardPlugins/Fix-Workflow.md), then [`HotZone`](StandardPlugins/CKli.HotZone.Plugin/README.md#how-the-fix-workflow) and [`Build`](StandardPlugins/CKli.Build.Plugin/README.md#fix-build--fix-publish-the-fix-workflow) |
| **`issue`** | Asks plugins to detect any possible issues. | [intrinsic](CKli.Core/CKliCommands/README.md#issue---all---fix) |
| **`layout`**<br/>`fix` `xif` | Reconciles the World definition file and the folders and repositories that are on the disk. | [intrinsic](CKli.Core/CKliCommands/README.md#layout-fix---delete-aliens) |
| **`log`** | Opens the log file of the last run. | [intrinsic](CKli.Core/CKliCommands/README.md#log---folder) |
| **`maintenance`**<br/>`migrate` `rebuild` | Rebuilds versions that have already been released. One-off conversions of repositories created under older CKli conventions. | [`Build`](StandardPlugins/CKli.Build.Plugin/README.md#a-note-on-rebuildoldasyncrebuildversionasync) and [`Migration`](StandardPlugins/CKli.Migration.Plugin/README.md#command) |
| **`plugin`**<br/>`add` `compile` `create` `disable` `enable` `info` `remove` `set` `unset` | Manages the plugins of the current World. | [intrinsic](CKli.Core/CKliCommands/README.md#plugin-commands-info-set-unset-create-add-remove-enable) |
| **`publish`** | Build-Test-Package and publish all the artifacts. | [`Build`](StandardPlugins/CKli.Build.Plugin/README.md#commands) and [`Publish`](StandardPlugins/CKli.Publish.Plugin/README.md) |
| **`*publish`** | Upstream closure publish: publishes the producers of the current repositories. | [`Build`](StandardPlugins/CKli.Build.Plugin/README.md#commands) and [`Publish`](StandardPlugins/CKli.Publish.Plugin/README.md) |
| **`pull`** | Pulls the Stack repository and all Repo's local branches that track a remote branch. | [intrinsic](CKli.Core/CKliCommands/README.md#pull---with-tags---all---continue-on-error---max-dop-n) |
| **`push`** | Pushes the Stack repository and all Repo's local branches that track a remote branch. | [intrinsic](CKli.Core/CKliCommands/README.md#push---stack-only---all---continue-on-error---max-dop-n) |
| **`remote`**<br/>`stack` | Operations on the remote repositories rather than on their local clones. | [intrinsic](CKli.Core/CKliCommands/README.md#remote-stack-migrate-newurl) |
| **`repo`**<br/>`add` `create` `remove` | Adds, creates and removes the Repositories of the current World. | [intrinsic](CKli.Core/CKliCommands/README.md#repo-add-url---allow-lts) |
| **`status`** | Lists the World's Repos, or every Stack that exists locally when outside a Stack. | [intrinsic](CKli.Core/CKliCommands/README.md#status---by-branch---all---skip-pull-stack) |
| **`tag`**<br/>`delete` `fetch` `list` `pull` `push` | Git tag operations on the current Repo or on all the Repos of the World. | [intrinsic](CKli.Core/CKliCommands/README.md#tag-commands-list-fetch-pull-push-delete) |
| **`update`** | Auto update CKli (must not be in interactive mode). | [intrinsic](CKli.Core/CKliCommands/README.md#update---version-version---stable---prerelease---allow-downgrade) |
| **`version`**<br/>`bump` `deprecate` | The CSemVer version tags of a repository: bumping and deprecating. | [`VersionTag`](StandardPlugins/CKli.VersionTag.Plugin/README.md#commands) |
| **`world`**<br/>`lock` `lts` `reference` `unlock` | The definition of the current World and the locks its Stack shares. | intrinsic: [references](CKli.Core/CKliCommands/README.md#world-commands-reference-list-set-remove), [LTS Worlds](CKli.Core/CKliCommands/README.md#world-lts-commands-create-clone) and [locks](CKli.Core/CKliCommands/README.md#world-lock-commands-lock-unlock) |

Those of the `Build` plugin are the two halves of what CKli automates and are worth knowing about even
before enabling anything: [`build` and `publish`](StandardPlugins/CKli.Build.Plugin/README.md) propagate
what a World **produces**, and
[`deps update`](StandardPlugins/CKli.Build.Plugin/README.md#deps-update-aligning-the-external-dependencies)
aligns what it **consumes** from the outside.

----
# Development & Local Testing

This repository holds the tool and the source of the 9 Standard Plugins. Each project documents its own
design:

| Project | |
|---|---|
| [`CKli.Core`](CKli.Core/README.md) | The Stack / World / Repo model, Git, the hosting providers, the secrets store and the plugin system. Its [`CKliCommands/`](CKli.Core/CKliCommands/README.md) folder holds the intrinsic commands and their reference. |
| [`CKli.Loader`](CKli.Loader/README.md) | The collectible `AssemblyLoadContext` the plugins are loaded into, and the assemblies the host shares with them. |
| [`CKli.Plugins.Core`](CKli.Plugins.Core/README.md) | What a plugin is written against, and the code generation that replaces reflection once plugins are compiled. |
| [`CKli.Testing`](CKli.Testing/README.md) | The fixture layer: fake local Git remotes and cloned folders to run real `ckli` commands with no network. |
| [`StandardPlugins/`](#the-basics-stack-world-repo) | The 9 Standard Plugins, listed with their READMEs above. |

There are 2 possible approaches to develop and test CKli itself.

## Temporarily replaces the currently installed CKli tool.

The [CompileAndInstallLocalCKli.ps1](CompileAndInstallLocalCKli.ps1) script fully rebuilds
`CK-SVersion` and `CKLi` and installs the tool in the `0.0.0-0` version.

```powershell
.\CompileAndInstallLocalCKli.ps1
```

When done, reinstall the current version (here the latest prerelease if it exists) from NuGet:
```powershell
ckli update
```

## Using an independent context (thanks to launchSettings.json).
This is possible thanks to the [`CKli/Properties/launchSettings.json`](CKli/Properties/launchSettings.json) file.
```json
{
  "profiles": {
    "C:\\Dev3 (ps)": {
      "environmentVariables": {
        "PATH": "$(SolutionDir)CKli/$(OutputPath);%PATH%"
      },
      "commandName": "Executable",
      "executablePath": "powershell",
      "workingDirectory": "$(SolutionDir)../CKliTestFolder"
    }
  }
}
```

It launches the compiled CKli instance in a `CKliTestFolder`.
This trick prepends the build output path to the `$Env:Path` (Linux/Mac: `$Path`): the `ckli.exe` is found here rather than in
the `%userprofile%\.nuget\packages` (Linux/Mac: `~/.nuget/packages`).

This only requires to manually create (once) the `CKliTestFolder` nearby the `CKli` folder.

In Visual Studio use **"Start Without Debugging (Ctrl+F5)"**: the running terminal can use your already running IDE
instance as the debugger when using the `ckli .... --ckli-debug` flag.
