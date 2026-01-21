# CKli

CKli is a tool for <u>multi-repositories</u> stacks.
It allows to automate actions (build, package upgrade, etc...), on <u>Worlds</u> (a group of repositories),
and concentrates information in a single place.

:warning: This is currently under development.

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
The `update` command is described below.

### Run CKli

If you installed CKli globally, you can run `ckli` in any command prompt to start it.

## Development & Local Testing

To test your local changes without publishing to NuGet:

```bash
# 1. Build and pack
dotnet build CKli.sln -c Debug
dotnet pack CKli/CKli.csproj -c Debug

# 2. Install as global tool (uninstall first if already installed)
dotnet tool uninstall -g CKli
dotnet tool install -g CKli --source ./CKli/bin/Debug --version 0.0.0-0

# 3. Test your changes
ckli --help
ckli log

# 4. When done, reinstall from NuGet
dotnet tool uninstall -g CKli
dotnet tool install -g CKli
```

## The basics: Stack-World-Repo

A World is a set of Git repositories. The set is described by a simple XML file that lists the
repositories and can organizes them in a folder structure:
```xml
<CK-Build>
  <Repository Url="https://github.com/CK-Build/CSemVer-Net" />
  <Repository Url="https://github.com/CK-Build/SGV-Net" />
  <Folder Name="Cake">
    <Repository Url="https://github.com/CK-Build/CodeCake" />
  </Folder>
</CK-Build>
```
This definition file is stored in the `main` branch of a Stack repository:

```
ckli clone https://github.com/CK-Build/CK-Build-Stack
```

A Stack contain at least one World: the default World that is the _current version of the Stack_.
Long Time Support (LTS) Worlds can be created any time from the a World (typically the default one).


## Private & Public stack and repositories
:warning: Explain PAT.

## Core commands
These commands are implemented by `CKli.Core`. They apply to any Git repositories.

### `update --stable --prerelease --allow-downgrade`

Auto updates CKli with a newer available version if it exists. Use `ckli --version` to display the
currently installed version.

By default, this will lookup for a stable version if the current version is a stable one.

With `--stable`, stable versions only will be considered even if the current version is a prerelease.

With `--prerelease`, prerelease versions will be considered (including CI builds) even if the current version is stable.

The `--allow-downgrade` flags allows package downgrade. This is Useful to come back to the last stable version when
the current version is a pre release.

This command transparently updates the CKli version used by the `CKli.Plugins` solution. If a `Tests/Plugins.Tests` project
exists, the version of the `CKli.Testing` package reference is also updated.

### `clone <url> --private --allow-duplicate`
Clones a Stack and all its current World repositories in the current directory.

`--private` drives the name of the Stack repository folder: it is `.PrivateStack/`
instead of `.PublicStack/`.

`--allow-duplicate` must be specified if the same Stack has already been cloned
and is available on the local system. In such case, the Stack's folder name will be
`Duplicate-Of-XXX/` instead of `XXX/`.

### `stack create <stackName> --url --private`
Creates a new Stack locally in the current directory. Unlike `clone`, this creates
a new empty stack rather than cloning from a remote.

The `<stackName>` should not include the `-Stack` suffix (it will be added automatically
to the remote URL if provided).

`--url` (or `-u`) optionally specifies a remote URL. This sets up the origin remote
but does not push. The remote repository must be created separately (e.g., on GitHub)
before pushing.

`--private` uses `.PrivateStack/` folder instead of `.PublicStack/`.

Example:
```bash
# Create a new stack with a remote URL
ckli stack create MyProject --url https://github.com/user/MyProject-Stack

# Create a local-only stack
ckli stack create MyLocalStack
```

### `stack info`
Displays information about the current Stack: name, path, remote URL, current branch,
and whether it's public or private.

Must be run from within a Stack directory.

### `stack set-remote-url <newUrl> --no-push`
Changes the Stack's remote URL (origin). This updates both the git remote configuration
and the local Stack registry.

By default, a push is attempted after changing the URL to verify the new remote is
accessible. Use `--no-push` to skip the push (useful when the remote doesn't exist yet
or when you want to verify the change before pushing).

The URL is validated and normalized (e.g., `.git` suffix is stripped).

Example:
```bash
# Change remote and push to verify
ckli stack set-remote-url https://github.com/neworg/MyProject-Stack

# Change remote without pushing
ckli stack set-remote-url https://github.com/neworg/MyProject-Stack --no-push
```

### `log --folder`
Opens the last log file. When `--folder` (or `-f`) is specified, the folder is opened instead
of the last log file.

The `Log/` folder is `%LocalAppData%/CKli/Out-of-Stack-Logs/` when CKli doesn't start is a Stack folder, otherwise
each Stack keeps its own logs in their `.PublicStack/Logs` (or `.PrivateStack/Logs`). 

### `pull -all --from-all-remotes --continue-on-error`

Pulls (fetch-merge) the Stack repository and all current Repos' local branches that track a remote branch.
Note that tags that point to the remote branches will be retrieved and will replace locally defined tags if they point
to the same object. If a local tag points to a different object, this will be an error.
To prevent this, use `ckli tag list` to detect conflicts.

By default, the current directory selects the Repos unless `--all` is specified.

`--from-all-remotes` fetches from all remotes instead of the 'origin' remote.

Any merge conflict is an error. Unless `--continue-on-error` is specified, the first error stops the operation.

A pull is implicitly executed first by `ckli push`. 

### `fetch -all --with-tags --from-all-remotes`
Fetches all branches (and optionally tags) of the current Repos.

By default, the current directory selects the Repos unless `--all` is specified.

When `--with-tags` is specified, remote tags will replace locally defined tags if they point
to the same object. If a local tag points to a different object, this will be an error.
Use `ckli tag list` to detect conflicts.

`--from-all-remotes` fetches from all remotes instead of the 'origin' remote.

### `push --stack-only -all --to-all-remotes --continue-on-error`
Pushes the Stack repository and all Repo's local branches that track a remote branch.
A pull is done before: it must be successful for the actual push to be done.

Tags are not pushed: tags are pushed when artifacts are published and this is the job
of dedicated plugins.

When `--stack-only` is specified, only the Stack repository is pushed. Repos are ignored.

By default, the current directory selects the Repos unless `--all` is specified.

`--to-all-remotes` considers all remotes instead of only the 'origin' remote.

Any conflict is an error. Unless `--continue-on-error` is specified, the first error stops
the push.

### `repo add <url> --allow-lts`
Adds a new repository to the current world.

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to add a new Repo to a Long Term Support World.

This clones the repository in the current directory, updates the World's definition file in
the Stack repository and creates a commit. To publish this addition, a `push` (typically
with `--stack-only`) must be executed.

### `repo remove <name or url> --allow-lts`
Removes an existing Repo from the current world.

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to remove a Repo from a Long Term Support World.

This deletes the local repository, updates the World's definition file in
the Stack repository and creates a commit. To publish this removal, a `push` (typically
with `--stack-only`) must be executed.

### `layout fix --delete-aliens`
Compares the local layout of folders and repositories with the World's definition file
and updates the local file system accordingly.
- Folders are moved and/or renamed to match the definition file (also fixes the difference in name casing).
- Missing Repo are cloned (where they must be).
- If `--delete-aliens` is specified, repositories not defined in the World are deleted.

### `layout xif`
Opposite of `fix`: consider the current folders and repositories to be the "right" definition of the World.
The World's definition file is updated and a commit is done in the Stack repository.

To publish this update, a `push` (typically with `--stack-only`) must be executed.

### `issue --all --fix`

Detects issues and display them or fix the issues that can be automatically fixed when `--fix` is specified.
When `--all` is specified, this applies to all the Repos of the current World (even if current path is in a Repo).

### `exec ... --ckli-continue-on-error --ckli-all`

This command execute any external process on the current Repo (or all of them if `--ckli-all` is specified).
By default, whenever a process fails on a repository (by returning a non 0 exit code), the loop stops: use
`--ckli-continue-on-error` flag to not stop on the first error.

The flags `--ckli-continue-on-error` and `--ckli-all` are not submitted to the process command line. (They are prefixed
by `--ckli-` to avoid a name clash with an existing process argument.)

Example: `ckli exec dotnet build --ckli-all` builds all the Repo of the Stack.

## Plugin commands

The core commands of CKli handles Stack, World and Repo (Git repositories).
The Repo can contain anything. To handle tasks specific to a technology (.NET, Node, Ruby, etc.)
external and optional plugins can be used.

Plugins are written in .NET and distributed as NuGet packages or can be source code directly
in the Stack repository.

### `plugin info --compile-mode`

Provides information on installed plugins, their state, Xml configuration element and an optional message
that can be produced by the plugin itself.

`--compile-mode` is an advanced option to be used when developing plugins. Plugins are discovered
once (after a creation, an install or a removal) via reflection and then compiled in `Release`
with generated code that replaces all the reflection.

A regular load is just an `Assembly.Load` (in a collectible `AssemblyLoadContext`) and a call to an
initialization function that initializes the graph of objects (command handlers, Plugin description, etc.).

In very specific scenario (developing, debugging), it is possible to set the compile mode to `None` (plugins
are not compiled, reflection is always used) or `Debug` to compile the plugins in debug configuration.

### `plugin create <name> --allow-lts`
Creates a new source based plugin project in the current World.

The name can be a short name ("MyFirstOne") or a full plugin name ("CKli.MyFirstOne.Plugin").

In a public World named "MyWorld", the code of the plugin is created in the `.PublicStack/MyWorld-Plugins/Ckli.MyFirstOne.Plugin/` folder.
It can be edited and tested freely.
The new plugin is added to the `<Plugins />` element of the world definition file:

```xml
<MyWorld>

  <Plugins>
    <MyFirstOne />
  </Plugins>

  <!-- Folders and Repositories... -->
</MyWorld>
```
The `<MyFirstOne />` element is the plugin configuration: the plugin code can read it to configure
its behavior and update it.

The new plugin will be "published" when `push` (typically with `--stack-only`) is executed.

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to add a new plugin to a Long Term Support World.

### `plugin remove <name> --allow-lts`
Removes a source based or package plugin from the current World.

The name can be the short name ("MyPlugin") or the full plugin name ("CKli.MyPlugin.Plugin").

:warning: Warnings:
- The removed plugin must not have dependent plugins otherwise this fails (and nothing is done).
- The plugins must not be globally disabled (see below).

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to remove a plugin from a Long Term Support World.

### `plugin add <packageId@version> --allow-lts`
Adds a new packaged plugin in the current World or updates its version.

When added, the plugin is added to the `<Plugins />` element of the world definition file,
just like in the source based scenario.

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to add a new plugin to a Long Term Support World.

The new plugin will be "published" when `push` (typically with `--stack-only`) is executed.

### `plugin disable <name>`
Plugins are enabled by default but can be disabled.

A `IsDisabled="true"` attribute is set on the corresponding plugin configuration element.
```xml
<MyWorld>

  <Plugins>
    <MyPlugin IsDisabled="true">
      <SomeOption>None</SomeOption>
    </MyPlugin>
    <MyPlugin />
  </Plugins>

  <!-- Folders and Repositories... -->
</MyWorld>
```
As usual, this modification will be "published" when `push` (typically with `--stack-only`) is executed.

### `plugin enable <name>`
Reverts the `plugin disable` command by removing the `IsDisabled="true"` attribute on the plugin configuration element.

As usual, this modification will be "published" when `push` (typically with `--stack-only`) is executed.

