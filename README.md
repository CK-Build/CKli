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
The `update` command handles switching from production and pre-releases easily (described below).

Stable versions are on [nuget.org](https://www.nuget.org/packages/CKli); the CI builds are on the
Signature-OpenSource feed. To manually install a specific version (replace `<version>` with the one you want):
```powershell
dotnet tool update CKli@<version> -g --prerelease --add-source https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json --no-http-cache
```

### Run CKli

If you installed CKli globally, you can run `ckli` in any command prompt to start it.

`ckli --help` lists the commands and namespaces; `ckli <command> --help` details one of them
(`--help` must be the last argument).

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
relocate something are rejected in interactive mode: `clone`, `create`, `lts clone`, `lts create`,
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
Long Time Support (LTS) Worlds can be created any time from a World (typically the default one).

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

### A Stack is public or private as a whole

There is no per-repository setting: **every Repo of a World inherits the flag of its Stack**. The Stack
folder name is where it is recorded — `.PublicStack/` or `.PrivateStack/` — and it is chosen once, by the
`--private` flag of [`clone`](#clone-url---lts-name-ltsname---max-dop-n---private---allow-duplicate---ignore-parent-stack---with-ref-clone---without-ref-clone)
or [`create`](#create-url---private---ignore-parent-stack).

What the flag actually changes is what needs a secret:

|  | Read (clone, fetch, pull) | Write (push, create a remote repository, publish a release) |
|---|---|---|
| Public Stack | no secret at all | a secret is required |
| Private Stack | a secret is required | a secret is required |

So a **write secret is always required**, even for a public Stack: only anonymous reading is free.

A Stack can reference another Stack that has its own flag: see `<Reference Private="true" />` in the
[`clone`](#clone-url---lts-name-ltsname---max-dop-n---private---allow-duplicate---ignore-parent-stack---with-ref-clone---without-ref-clone)
and [`world reference set`](#world-reference-set-stackurlorname---lts-name-ltsname---default-world---no-default-clone---default-clone---private---public---allow-lts)
sections. A public Stack cannot reference a private one.

### The secrets are never in the definition file

A World definition file only ever contains the **name of a key**, never a secret value. The value is
resolved at runtime from a secret store, and the default store is
[.NET user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) under the `CKli`
identifier:

```powershell
dotnet user-secrets set GITHUB_CK_BUILD_WRITE_PAT <<your-token>> --id CKli
```

**You never have to work the key name out yourself.** When an operation needs a secret that is not
registered, CKli stops and logs the exact command line to run, with the key it wants — and, when a
stronger key would also do, the alternatives.

The secret is used as the password of a `CKli` user name, so a Personal Access Token (PAT) is what a Git
hosting provider expects here. NuGet feed API keys go in the same store, under the key each `<Feed>`
element names: see
[`CKli.ArtifactHandler.Plugin`'s README](StandardPlugins/CKli.ArtifactHandler.Plugin/README.md#nuget-feed-configuration--nugetfeed--nugetfeedcredentials)
(and note that a `<PublicReadCredentials>` is the exception: its two values are literals, not store keys).

### How a key name is derived

Key names are **computed from the remote url**, they are not configured. A common `{Prefix}` is derived
first, then `_READ_PAT` and `_WRITE_PAT` are appended to it.

For the known cloud providers, the prefix is a provider name followed by the **first path segment of the
url** — the owner, organization or workspace:

| Url | `{Prefix}` |
|---|---|
| `https://github.com/CK-Build/CKli` | `GITHUB_CK_BUILD` |
| `https://gitlab.com/acme/some-repo` | `GITLAB_ACME` |
| `https://dev.azure.com/Signature-OpenSource/...` | `AZUREDEVOPS_SIGNATURE_OPENSOURCE` |
| `https://bitbucket.org/acme/some-repo` | `BITBUCKET_ACME` |

Everything is uppercased and any character outside `A-Z`, `0-9` and `_` becomes `_` — which is why
`CK-Build` gives `CK_BUILD`.

For a self-hosted provider, the prefix is the **authority** of the url, uppercased and secured the same
way: `https://gitlab.acme.com/team/x` gives `GITLAB_ACME_COM`.

A key is enough for Git itself, but the commands that talk to the host's own API — creating a remote
repository, archiving one, publishing a release — need a *hosting provider*, and CKli only has three:
GitHub, GitLab and Gitea. They are selected by `github.com` and `gitlab.com`, or by a self-hosted
authority that contains `github`, `gitlab` or `gitea`. Azure DevOps and Bitbucket urls get a key and
nothing else, and so does any unrecognized authority.

Two consequences worth knowing:

- **A key covers an owner, not a repository.** One `GITHUB_CK_BUILD_WRITE_PAT` is enough for every
  repository of the `CK-Build` organization, and a Stack whose repositories span two organizations needs
  one key per organization.
- **A `file://` remote is the exception.** Its key is the bare `FILESYSTEM_GIT`, with no `_READ_PAT` /
  `_WRITE_PAT` suffix. Its value is never actually used, but it must exist for a push to be attempted —
  registering it is the way to state that pushing to the local file system is intended.

### Read and write keys

`{Prefix}_WRITE_PAT` is the stronger of the two: a read first looks for it, then falls back to
`{Prefix}_READ_PAT`. So registering the write key alone is enough for everything, and a read-only key is
only useful to give someone clone access without push access.

The write key must let CKli push. Beyond that, the commands that reach the hosting provider's API need
more from it:

- **creating a repository** — [`create`](#create-url---private---ignore-parent-stack),
  [`repo create`](#repo-create-url---allow-lts) and
  [`remote stack migrate`](#remote-stack-migrate-newurl). The first two also **delete** the repository
  they just created when the rest of the operation fails, so the key must allow that too;
- **archiving a repository** and **changing its default branch** —
  [`remote stack migrate`](#remote-stack-migrate-newurl) and the publication;
- **creating, uploading to and finalizing releases** — the `Publish` plugin.

On GitHub, that maps to the `repo` scope of a classic token — plus `delete_repo` if you want the rollback
above to work — or, for a fine-grained token, Contents (read/write) and Administration (read/write) on
the organization.

## Core commands
These commands are implemented by `CKli.Core`. They apply to any Git repositories.

### `update --stable --prerelease --allow-downgrade --dry-run`

Auto updates CKli with a newer available version if it exists. Use `ckli --version` to display the
currently installed version.

By default, this will lookup for a stable version if the current version is a stable one.

With `--stable`, stable versions only will be considered even if the current version is a prerelease.

With `--prerelease`, prerelease versions will be considered (including CI builds) even if the current version is stable.

The `--allow-downgrade` flags allows package downgrade. This is useful to come back to the last stable version when
the current version is a pre release.

`--dry-run` (or `-d`) only displays the `dotnet tool update` command line that would be run.

This command transparently updates the CKli version used by the `CKli.Plugins` solution. If a `Tests/Plugins.Tests`
project exists in the `{WorldName}-Plugins` solution, the version of the `CKli.Testing` package reference is also updated.

This command is rejected in interactive mode.

### `clone <url> --lts-name <@ltsName> --max-dop <n> --private --allow-duplicate --ignore-parent-stack --with-ref-clone --without-ref-clone`
Clones a Stack and the repositories of one of its Worlds in the current directory: its default World unless
`--lts-name` is specified.

`--lts-name <@ltsName>` clones the repositories of that Long Term Support World instead, in the Stack's own
`@ltsName/` folder. The Stack must have that World (see [`lts create`](#lts-create-ltsname)); to obtain a
second World of an already cloned Stack, use [`lts clone`](#lts-clone-ltsname).

`--max-dop <n>` limits the parallelism when cloning the repositories.

`--private` declares the Stack (and therefore every repository of its Worlds) private: the Stack folder
is `.PrivateStack/` instead of `.PublicStack/`, and a read PAT is required to clone at all — see
[Private & Public stack and repositories](#private--public-stack-and-repositories).

`--allow-duplicate` must be specified if the same Stack has already been cloned
and is available on the local system. In such case, the Stack's folder name will be
`Duplicate-Of-XXX/` instead of `XXX/`.

`--ignore-parent-stack` allows the cloned Stack to be inside an existing one.

Once the Stack is cloned, the `<Reference Url="..." />` elements of its default World definition are handled:
a reference names another Stack that this World uses and it is cloned **next to** this one (never inside it),
recursively. A referenced Stack that is already cloned on this system is left as-is and a cycle between
Stacks is handled: each Stack is cloned once.

A reference is a public Stack unless it carries `Private="true"` (and a public Stack cannot reference a
private one: this is an error). A reference with `DefaultClone="false"` is skipped by default.

A reference that carries `LTSName="@net8"` selects the Long Term Support World of the referenced Stack that is
used: the repositories of **that** World are the ones cloned (in the referenced Stack's `@net8/` folder rather
than at its root), and the recursion then follows that World's own references. A referenced Stack that has no
such World is an error.

A Stack is cloned only once, so when two references name the same Stack with two different Worlds, the second
World is **added** to the clone rather than cloned again — exactly what `lts clone` does — and its own
references are then followed too. When the Stack was instead found already cloned somewhere else on this
machine, that folder is left untouched and the `lts clone` command line to run there is reported.

`--with-ref-clone` clones every reference, including the `DefaultClone="false"` ones.

`--without-ref-clone` clones no reference at all.

The references themselves are managed by the [`world reference`](#world-commands-reference-list-set-remove) commands.

### `create <url> --private --ignore-parent-stack`
Creates a new Stack by creating the remote repository (the url must belong to a Git hosting provider
that CKli can handle), checks out the new stack in the current directory, initializes default files in
the stack folder and pushes it.

The `<url>` must end with the `-Stack` suffix.

`--private` creates a private remote repository and uses the `.PrivateStack/` folder instead of
`.PublicStack/`. Whatever the choice, a write PAT is required — see
[Private & Public stack and repositories](#private--public-stack-and-repositories).

`--ignore-parent-stack` allows the new Stack to be created inside an existing one.

If anything fails after the remote repository has been created, CKli deletes it again.

This command is rejected in interactive mode.

### `remote stack migrate <newUrl>`
Moves the Stack repository to a new remote: creates the new remote repository if it doesn't exist yet,
changes the `origin` url, pushes the Stack content and, on success, archives the previous repository.

The `<newUrl>` must end with the `-Stack` suffix and the Stack name must not change: a migration cannot
rename a Stack (the Stack folder name and the World definition file names are the Stack name).

Only the Stack repository moves: the repositories of the Worlds keep their own remotes.

The command is as idempotent as it can be. It first detects whether the url has already moved to `<newUrl>`
and it remembers the previous url (in the `ckli.migratedFrom` local git configuration) until that previous
repository has reached its final state. A run interrupted between the url change and the archive is
therefore finished by the next one, and a run on an already migrated Stack changes nothing.

When the hosting provider cannot archive a repository (the file system one cannot), a warning is emitted and
the previous repository is left as-is: it should then be archived or deleted manually.

Both urls need a write PAT, and they may need two different ones: the key is derived from the url's owner,
so moving a Stack to another organization means registering a key for that organization too — see
[Private & Public stack and repositories](#private--public-stack-and-repositories).

### `log --folder`
Opens the last log file. When `--folder` (or `-f`) is specified, the folder is opened instead
of the last log file.

The `Log/` folder is `%LocalAppData%/CKli/Out-of-Stack-Logs/` when CKli doesn't start is a Stack folder, otherwise
each Stack keeps its own logs in their `.PublicStack/Logs` (or `.PrivateStack/Logs`). 

### `pull --with-tags --all --continue-on-error --max-dop <n>`

Pulls (fetch-merge) the Stack repository and all current Repos' local branches that track a remote branch.
By default, remote tags are safely fetched, preserving local tags (see `cki tag fetch`). When `--with-tags`
is specified, a `ckli tag pull *` is done that blindly replaces local tags.

By default, the current directory selects the Repos unless `--all` is specified.

Any merge conflict is an error. Unless `--continue-on-error` is specified, the first error stops the operation.

`--max-dop <n>` limits the parallelism.

A pull (without tags) is implicitly executed first by `ckli push`. 

### `fetch --all --with-tags --max-dop <n>`
Fetches all branches (and optionally the tags that are associated to any fetched objects) in the current Repos.

By default, the current directory selects the Repos unless `--all` is specified.

`--max-dop <n>` limits the parallelism.

When `--with-tags` is specified, the fetched remote tags will replace locally defined tags if
they reference the same object. If a local tag references a different object, this will be an error.
Use `ckli tag list` to detect conflicts.

### `branch push <branch> --all`
Pushes the specified branch to its remote "origin", creating it if it doesn't exist yet.
The branch is fetched and must be successfully merged before the push can succeed.

By default, the current directory selects the Repos unless `--all` is specified.
When applied to multiple Repos, a warning is emitted if the branch doesn't exist in a Repo.

A branch in the `local/` or `building/` namespace cannot be pushed (see `ckli push`).

### `push --stack-only --all --continue-on-error`
Pushes the Stack repository and all Repo's local branches that track a remote branch.
A pull is done before: it must be successful for the actual push to be done.

Tags are not pushed: tags are pushed when artifacts are published and this is the job
of dedicated plugins.

References in the `local/` and `building/` namespaces are never pushed, whatever the command: they are the
version tags of a build that is in progress or not published yet, purely local artifacts that any other
clone recomputes (and a pushed `local/` version tag breaks the fix branch adoption of `ckli fix start`).
Such a branch is skipped with a warning here; `ckli branch push` and `ckli tag push` reject it with an error.
Deleting such a reference from a remote remains possible.

When `--stack-only` is specified, only the Stack repository is pushed. Repos are ignored.

By default, the current directory selects the Repos unless `--all` is specified.

Any conflict is an error. Unless `--continue-on-error` is specified, the first error stops
the push.

### `status --by-branch --all --skip-pull-stack`

When the current directory is in a World, lists the Repos with their folder path, current branch name,
remote commit diffs, and remote origin url.

Otherwise, this lists every Stack that is registered on this machine, with its root folder and whether
it is public or private.

When `--by-branch` (or `-b`) is specified, repositories are grouped by their current branch name
instead of being listed in definition order.

By default, the current directory selects the Repos unless `--all` is specified.

`--skip-pull-stack` doesn't update the Stack repository first.

### `repo add <url> --allow-lts`
Adds a new repository to the current world.

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to add a new Repo to a Long Term Support World.

This clones the repository in the current directory, updates the World's definition file in
the Stack repository and creates a commit. To publish this addition, a `push` (typically
with `--stack-only`) must be executed.

### `repo create <url> --allow-lts`
Same as `repo add` but the remote repository doesn't exist yet: it is created first (the url must belong
to a Git hosting provider that CKli can handle, and a write PAT is required), then cloned in the current
directory and added to the World's definition file. If anything fails after the creation, the remote
repository is deleted again.

The new repository is created private or public according to the Stack: see
[Private & Public stack and repositories](#private--public-stack-and-repositories).

As for `repo add`, `--allow-lts` is required when the current World is a LTS one, and a `push` (typically
with `--stack-only`) publishes the addition.

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

Examples:
- `ckli exec dotnet build --ckli-all` builds all the Repo of the Stack (the current state of the working folder).
- `ckli exec git pull --tags --force` updates all the tags from the remotes, replacing the local ones (kind of `ckli tag pull *` that
   is not currently supported).

## LTS World commands (create, clone)

A Stack has one default World and any number of Long Term Support Worlds. A World is defined by a
`{StackName}@{ltsName}.xml` file in the Stack repository and its repositories live in the Stack's own
`@ltsName/` folder — so the Worlds of a Stack are siblings, not copies of it. A LTS name must be at
least 3 characters starting with `@`, then only ASCII lowercase letters, digits, `-`, `_` and `.`.

### `lts create <@ltsName>`
Creates a new LTS World from the current default World. Must be run from the default World.

The new World's definition file is a clone of the current one, and the plugins are given the opportunity to
adjust it through the `WorldEvents.CreateLTS` event. The
[VersionTag plugin](StandardPlugins/CKli.VersionTag.Plugin/README.md#worldeventscreatelts--cutting-the-version-range-of-a-new-lts)
uses it to split the version range — the new World keeps the versions produced so far and the default World
starts a new Major above them — and to reduce the new World's branch model to its root branch.

The World must be **fully published** for this to be possible: no version or branch issue anywhere, every
repository currently offering a published version (no `+fake`, no `+deprecated`), no pending `local/` or
`building/` release left anywhere, and every `dev/` root branch integrated. Otherwise the command fails,
listing every repository that is in the way, and nothing is written — neither the new definition file nor the
`InfVersion` the current World would have received.

The pending-release rule is the one that bites in practice: a `local/` build is below the cut, so it would end
up in the new LTS World while the code it came from stays in the default one. Publish it (or let a new build
supersede it) first.

Only the definition file is created. The new World's repositories and its plugin solution appear when it is
first opened, which is what [`lts clone`](#lts-clone-ltsname) does.

### `lts clone <@ltsName>`
Clones the repositories of an existing LTS World of the current Stack into its `@ltsName/` folder. Use this
when the Stack is already cloned; `clone --lts-name` is the equivalent for a Stack that is not.

Only the missing repositories are cloned, so running it again does nothing. Opening a World for the first
time also generates its plugin solution inside the Stack repository, so the command commits the Stack: no
`push` is needed for the World to exist locally, but a `push --stack-only` publishes that plugin solution.

## World commands (reference list, set, remove)

These commands manage the `<Reference Url="..." />` elements of the current World's definition file: the other
Stacks that this World uses and that the `clone` command clones next to it. They exist so that the definition
file never has to be edited by hand.

Like `repo add`/`repo remove`, `set` and `remove` update the World's definition file in the Stack repository and
create a commit: to publish the change, a `push` (typically with `--stack-only`) must be executed. If the current
World is a LTS one (`CK@Net8`), `--allow-lts` must be specified — and a warning is emitted because `clone` honors
the references of the **default** World only: a reference declared in a LTS World is never cloned.

### `world reference list`
Lists the references of the current World with their `DefaultClone`, `Private` and `LTSName` state and where each
referenced Stack is cloned on this machine (or that it is not).

### `world reference set <stackUrlOrName> --lts-name <@ltsName> --default-world --no-default-clone --default-clone --private --public --allow-lts`
Creates or updates a reference. The `<stackUrlOrName>` is the url of the referenced Stack; the **name** of a Stack
that is cloned on this machine (`CK-Database` or `CK-Database-Stack`) can be used instead and is resolved to its url.
The url must have the `-Stack` suffix and a Stack cannot reference itself.

This **merges**: only the attributes named by the option or the flags are changed, so `world reference set <url>` on
an existing reference just makes sure it is there and leaves its attributes as they are.

`--lts-name <@ltsName>` sets `LTSName="@net8"`: the Long Term Support World of the referenced Stack that this World
uses. Unlike the 2 booleans below, this attribute has no default value — when it is absent, the referenced Stack's
**default** World is the one that is used — so `--default-world` is what removes it. The 2 are mutually exclusive and
the name must satisfy the same rule as `lts create`: at least 3 characters starting with `@`, then only ASCII
lowercase letters, digits, `-`, `_` and `.`.

`clone` honors it: the repositories of that LTS World are the ones cloned, in the referenced Stack's
`@ltsName/` folder. A referenced Stack that has no such World fails the clone.

`--no-default-clone` sets `DefaultClone="false"` (`clone` then skips this reference unless `--with-ref-clone` is used)
and `--default-clone` removes the attribute since `true` is its default value.

`--private` sets `Private="true"` and `--public` removes the attribute (`false` is its default value). Each pair is
mutually exclusive. A public Stack cannot reference a private one: this is refused (writing it would produce a
definition file that can no more be loaded).

When the referenced Stack is cloned on this machine, its `.PublicStack/`or `.PrivateStack/` folder is the ground
truth: `Private="true"` is inferred when the reference is created without `--private` nor `--public`, an explicit
flag that contradicts it is an error, and an existing reference that contradicts it is reported as a warning.

### `world reference remove <nameOrUrl> --allow-lts`
Removes a reference. The `<nameOrUrl>` is its url, the referenced repository name (`XXX-Stack`) or its stack
name (`XXX`) — the url is required when a name matches more than one reference. Removing a reference that doesn't
exist is not an error, and a `<References>` element that becomes empty is removed.

## Tag commands (list, fetch, pull, push, delete) 

### `tag list --local --remote --diff-only --all`
Lists local tags and/or remote tags from the current Repo or all the Repos.

By default (without `--local` or `--remote`), both local and remote tags are fetched
and a diff is displayed showing tags that exist only locally, only remotely, or in both.

When `--local` is specified, only local tags are listed.

When `--remote` is specified, only remote tags are listed.

When `--diff-only` is specified, only the differences between local and remote tags are displayed.

When `--all` is specified, lists tags for all the Repos of the current World
(even if the current path is in a Repo).

### `tag fetch --all`
Safe "tag pull" that preserves local tags and local tags in conflicts. This creates only new local tags.

When `--all` is specified, lists tags for all the Repos of the current World
(even if the current path is in a Repo).

### `tag pull <tag names> --allow-multi-repo`
Pulls the specified tags from the remote "origin" into the current Repo.
Local modifications of fetched tags are lost, conflicts are solved: remote always wins.

Tag names must contain only ASCII characters with lowercase letters (to avoid case sensitivity issues).
They can be the "canonical names" that starts with "refs/tags/" or simple names (like "v4.0").
The very basic glob capabilities of https://git-scm.com/docs/git-fetch are supported: `ckli tag pull *`
pulls all the remote tags (this is equivalent to `git pull --tags --force`).

Must be run from within a Repo directory unless `--allow-multi-repo` is specified.

### `tag push <tag names> --allow-multi-repo`
Pushes the specified tags from the current Repo to its remote "origin".
Modifications of remote tags are lost (the local version replaces them).

Tag names must contain only ASCII characters with lowercase letters (to avoid case sensitivity issues).

Tags in the `local/` and `building/` namespaces cannot be pushed (see `ckli push`).

Must be run from within a Repo directory unless `--allow-multi-repo` is specified.

### `tag delete <tag names> --with-remote --remote-only --allow-multi-repo`
Deletes local tags (and/or optionally from the remote "origin").
The operation is idempotent: tags that don't exist are silently ignored.

By default, only local tags are deleted and the command must be run from within a single Repo.

`--with-remote` deletes tags from both local and remote.

`--remote-only` deletes remote tags only, keeping local ones.

`--allow-multi-repo` allows the command to proceed when the current path is above multiple Repos.
By default, the current path must be within a single Repo.

## Dependency commands (deps update)

Unlike every other command described above, this one comes from a **plugin** (the Standard
`CKli.Build.Plugin`), so it exists only in a World that enables it. It is mentioned here because it is the
"package upgrade" half of what CKli automates - the `build` and `publish` commands are the other half, and
they are documented with it in
[`CKli.Build.Plugin`'s README](StandardPlugins/CKli.Build.Plugin/README.md#deps-update-aligning-the-external-dependencies).

### `deps update --branch <name> --all --narrow --no-fetch --ci --prerelease --stable --allow-downgrade --dry-run`

Aligns the **external** package dependencies of a World: the packages its repositories consume but don't
produce. A target version comes from a `<VersionTag><Packages>` pin (which means "never touch this"), from
the published profiles of the World `<Reference>`s, or - for the identifiers no reference anchors - from the
greatest version the World's feeds offer.

It updates the pivot repositories, the upstreams that need it, and by default the downstreams of everything it
updates (`--narrow` keeps it to the upstreams): that is what a `ckli build` afterwards would rebuild anyway.

The World must be up to date and clean: the command fetches, then **refuses** to run when a branch is behind
its remote rather than merging it for you - run `ckli pull` first. A repository that doesn't have the branch
yet gets it created, at the commit its branch model says it must start from.

Use `--dry-run` to see the report without writing anything. Downgrades are reported apart and require
`--allow-downgrade` to be applied.

## Plugin commands (info, create, add, remove, enable)

The core commands of CKli handles Stack, World and Repo (Git repositories).
The Repo can contain anything. To handle tasks specific to a technology (.NET, Node, Ruby, etc.)
external and optional plugins can be used.

Plugins are written in .NET and distributed as NuGet packages or can be source code directly
in the Stack repository.

### `plugin info --skip-pull-stack`

Provides information on installed plugins, their state, Xml configuration element and an optional message
that can be produced by the plugin itself.

`--skip-pull-stack` doesn't update the Stack repository first.

### `plugin compile --mode <None|Debug|Release> --skip-pull-stack`

Plugins are discovered once (after a creation, an install or a removal) via reflection and then compiled
(in `Release` mode by default) with generated code that replaces all the reflection.
Once compiled, a regular load is just an `Assembly.Load` (in a collectible `AssemblyLoadContext`) and a call to a
single static initialization function that initializes the graph of objects (command handlers, Plugin description, etc.).

In very specific scenario (developing, debugging), it is possible to set the option `--mode` to `None` (plugins
are not compiled, reflection is always used) or `Debug` to compile the plugins in debug configuration.

`--skip-pull-stack` doesn't update the Stack repository first.

**Whenever a command changes — created, removed, or its flags, options, parameters or return type edited —
delete the generated `CKli.CompiledPlugins.cs` and run this command.** That file is the exact transcription of
the `[CommandPath]` methods and nothing keeps it in step on its own: the `_configSignature` it carries detects a
changed `<Plugins>` *configuration*, not a changed command *signature*. It must never be hand-edited to catch
up — see [`CKli.Plugins.Core`'s README](CKli.Plugins.Core/README.md#reflectionplugincollectorfactory-reflection-execution-vs-code-generation)
for why a stale one can still compile and still be accepted.

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

### `plugin add <packageId@version> --allow-lts`
Adds a new packaged plugin in the current World or updates its version.

When added, the plugin is added to the `<Plugins />` element of the world definition file,
just like in the source based scenario.

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to add a new plugin to a Long Term Support World.

The new plugin will be "published" when `push` (typically with `--stack-only`) is executed.

### `plugin remove <name> --allow-lts`
Removes a source based or package plugin from the current World.

The name can be the short name ("MyPlugin") or the full plugin name ("CKli.MyPlugin.Plugin").

:warning: Warnings:
- The removed plugin must not have dependent plugins otherwise this fails (and nothing is done).
- The plugins must not be globally disabled (see below).

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to remove a plugin from a Long Term Support World.

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

----
# Development & Local Testing

This repository holds the tool and the source of the 9 Standard Plugins. Each project documents its own
design:

| Project | |
|---|---|
| [`CKli.Core`](CKli.Core/README.md) | The Stack / World / Repo model, Git, the hosting providers, the secrets store, the core commands and the plugin system. |
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
