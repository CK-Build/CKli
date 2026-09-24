# The intrinsic `ckli` commands

This folder holds one `Command` class per **intrinsic** command: the commands CKli itself implements, as
opposed to the ones a plugin brings. They handle Stack, World, Repo and Git and know nothing of what a
repository *contains* — which is exactly why they work on any Git repository, whatever its technology.

This document is their reference. The map of **all** the commands — these plus the ones the enabled plugins
add — is in the [root README](../../README.md#the-commands). How a command is declared, parsed, dispatched
and helped is in [`CKli.Core`'s README](../README.md#commands).

> Each `###` heading below carries the command **and its flags**, so a new flag belongs in the heading as
> well as in the body.

## Core commands
These commands are implemented by `CKli.Core`. They apply to any Git repositories.

### `update --version <version> --stable --prerelease --allow-downgrade`

Auto updates CKli with a newer available version if it exists. Use `ckli --version` to display the
currently installed version.

By default, this will lookup for a stable version if the current version is a stable one.

With `--stable`, stable versions only will be considered even if the current version is a prerelease.

With `--prerelease`, prerelease versions will be considered (including CI builds) even if the current version is stable.

The `--allow-downgrade` flags allows package downgrade. This is useful to come back to the last stable version when
the current version is a pre release.

`--version <version>` installs that exact version instead of the latest one. The tool comes from the
`Signature-OpenSource` feed, which carries every CKli version (prereleases and CI builds included).

**In a Long Term Support World** (`StackRoot/@ltsName/` or below), CKli is frozen on the World's pinned version,
the local tool that `dotnet ckli` runs: there, `--version` is required and the command moves the World to that
CKli, in-process and in 3 ordered steps:

1. the World's local tool (this checks that the version exists, and refuses a downgrade without
   `--allow-downgrade`: then nothing is changed);
2. the `CKliVersion` attribute of `@ltsName/{StackName}@ltsName.xml`, committed and **pushed** in the Stack
   repository: the other developers get the new pin with their next pull, and its mismatch installs it as their
   local tool;
3. the `CKli.Version.props` of the World's plugin solution, deleted: its regeneration by the next command is what
   recompiles the plugins against the new CKli.

These steps cannot be atomic: on error, fix the cause and run the command again. If it is not run again, the next
command in the World finds a local tool that is not the pinned one and moves it back to the pin.

This command transparently updates the CKli version used by the `CKli.Plugins` solution. If a `Tests/Plugins.Tests`
project exists in the `{WorldName}-Plugins` solution, the version of the `CKli.Testing` package reference is also updated.

This command is rejected in interactive mode.

### `clone <url> --lts-name <@ltsName> --max-dop <n> --private --allow-duplicate --ignore-parent-stack --with-ref-clone --without-ref-clone`
Clones a Stack and the repositories of one of its Worlds in the current directory: its default World unless
`--lts-name` is specified.

`--lts-name <@ltsName>` clones the repositories of that Long Term Support World instead, in the Stack's own
`@ltsName/` folder. The Stack must have that World (see [`world lts create`](#world-lts-create-ltsname)); to obtain a
second World of an already cloned Stack, use [`world lts clone`](#world-lts-clone-ltsname).

`--max-dop <n>` limits the parallelism when cloning the repositories.

`--private` declares the Stack (and therefore every repository of its Worlds) private: the Stack folder
is `.PrivateStack/` instead of `.PublicStack/`, and a read PAT is required to clone at all — see
[Private & Public stack and repositories](../../README.md#private--public-stack-and-repositories).

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

A reference **without** `LTSName` of a Long Term Support World (`clone --lts-name @net8`) uses the referenced
Stack's World with the same LTS name (`@net8`) when it has one, and its default World otherwise: a LTS World
naturally depends on the LTS Worlds of its references.

A Stack is cloned only once, so when two references name the same Stack with two different Worlds, the second
World is **added** to the clone rather than cloned again — exactly what `world lts clone` does — and its own
references are then followed too. When the Stack was instead found already cloned somewhere else on this
machine, that folder is left untouched and the `world lts clone` command line to run there is reported.

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
[Private & Public stack and repositories](../../README.md#private--public-stack-and-repositories).

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
[Private & Public stack and repositories](../../README.md#private--public-stack-and-repositories).

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

### `push --stack-only --all --continue-on-error --max-dop <n>`
Pushes the Stack repository and all Repo's local branches that track a remote branch.
A pull is done before: it must be successful for the actual push to be done.

The Repos are independent: they are pulled and then pushed in parallel and each of them is pushed by a
single network operation (all its branches at once). `--max-dop <n>` limits the parallelism of both phases.
The Stack repository itself is always pushed first and alone.

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
[Private & Public stack and repositories](../../README.md#private--public-stack-and-repositories).

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
the name must satisfy the same rule as `world lts create`: at least 3 characters starting with `@`, then only ASCII
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

## World LTS commands (create, clone)

A Stack has one default World and any number of Long Term Support Worlds. A LTS World is defined by a
`@{ltsName}/{StackName}@{ltsName}.xml` file in the Stack repository — its own folder there, beside its plugin
solution and its `Published/` folder — and its repositories live in the Stack's own `@ltsName/` folder — so
the Worlds of a Stack are siblings, not copies of it. A LTS name must be at
least 3 characters starting with `@`, then only ASCII lowercase letters, digits, `-`, `_` and `.`.

> **In a LTS World, use `dotnet ckli` instead of `ckli`.** A LTS World is pinned to the CKli version that
> created it, and the global `ckli` follows the latest version. Cloning a LTS World (`world lts clone`,
> `clone --lts-name`, the end of `world lts create`) installs its pinned CKli as a **local .NET tool** in its
> `@ltsName/` folder: `dotnet new tool-manifest` then
> `dotnet tool update CKli --version <pin> --source https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json`
> (the feed that carries every CKli version, prereleases included). `dotnet ckli`, run in that folder or below,
> launches it. When the global `ckli` is used there anyway, it refuses to open the World: it installs the local
> tool then (if it wasn't) and tells you to retry with `dotnet ckli`. In a LTS World, `update --version <version>`
> moves the World to another CKli (see [`update`](#update---version-version---stable---prerelease---allow-downgrade)).

### `world lts create <@ltsName>`
Creates a new LTS World from the current default World. Must be run from the default World. The CKli Stack
itself refuses it: its plugin solution references the Standard Plugins by source, which a snapshot in the
`@ltsName/` folder cannot follow.

The command holds two [World locks](#world-lock-commands-lock-unlock) of the default World for its whole
duration: **`publish`** — nobody publishes while the version ranges are cut — and **`lts`** — the LTS Worlds of a
Stack are created one at a time. Another developer holding either of them makes the command fail, naming the
holder; a reservation of this very clone (`ckli world lock publish`) is renewed rather than refused. Both are
released when the command ends.

The new World's definition file is a clone of the current one, and the plugins are given the opportunity to
adjust it through the `WorldEvents.CreateLTS` event. The
[VersionTag plugin](../../StandardPlugins/CKli.VersionTag.Plugin/README.md#worldeventscreatelts--cutting-the-version-range-of-a-new-lts)
uses it to split the version range — the new World keeps the versions produced so far and the default World
starts a new Major above them — and to reduce the new World's branch model to its root branch.

The World must be **fully published** for this to be possible — publishing it first (`ckli publish --release`) is
the user's job: no version or branch issue anywhere, every repository currently offering a published version (no
`+fake`, no `+deprecated`), no pending `local/` or `building/` release left anywhere, **no `dev/` root branch at
all** (neither local nor on the remote: a publication integrates and deletes it), and every root branch equal to
its remote one (the repositories are fetched: a teammate's publication since the last pull would otherwise be left
below the cut, in neither World). Otherwise the command fails,
listing every repository that is in the way, and nothing is written — neither the new definition file nor the
`InfVersion` the current World would have received.

The pending-release rule is the one that bites in practice: a `local/` build is below the cut, so it would end
up in the new LTS World while the code it came from stays in the default one. Publish it (or let a new build
supersede it) first.

The new World is **pinned** to the CKli version that creates it: its root element receives a
`CKliVersion="0.13.0"` attribute, and CKli then refuses to open that World with any other version — installing
the pinned one as the World's local tool, so that `dotnet ckli` is all it takes to retry. This is the point of a LTS World — it is frozen, so its plugins must not be built against a
CKli it has never seen. The default World carries no such attribute: there, each developer keeps its own CKli
version. There is no flag to skip the pin; removing it is a manual edit of the definition file, like
`MinCKliVersion`.

One thing is deliberately **not** cloned: the `LockPrefix` attribute. It states the Git reference namespace
that locks the Stack, and since every World of a Stack locks in the one Stack repository it is a Stack level
setting that only the default World carries — a copy on the new World could only diverge from the one that is
actually used. See
[`LockPrefix`](../README.md#lockprefix-the-reference-namespace-that-locks-the-stack).

Once every plugin has accepted the creation, the new World gets its own folders: `@ltsName/` in the Stack
repository and `@ltsName/` in its git ignored `$Local` folder. What the new World starts with is written there:

- its definition file, `@ltsName/{StackName}@ltsName.xml`;
- its **root branch**, `@ltsName/stable`, created in every repository on the commit of its last published version
  and **pushed**, by the
  [VersionTag plugin](../../StandardPlugins/CKli.VersionTag.Plugin/README.md#worldeventscreatelts--cutting-the-version-range-of-a-new-lts).
  The LTS World starts with the last published version; whatever comes after it stays in the default World;
- a **snapshot of the plugin solution**: `{StackName}-Plugins/` is copied to `@ltsName/{StackName}-Plugins@ltsName/`
  (its git ignored files excepted: they are regenerated), its `.slnx` renamed and its build output redirected to
  `$Local/@ltsName/`. The plugins a LTS World uses during its whole life are the ones it has been created with;
- a copy of the `Common/` folder, by the
  [CommonFiles plugin](../../StandardPlugins/CKli.CommonFiles.Plugin/README.md#long-term-support-worlds);
- the `TestRun.Sha.txt` cache of the successful test runs, **moved** to `$Local/@ltsName/` by the
  [Build plugin](../../StandardPlugins/CKli.Build.Plugin/README.md): the LTS World is the one that keeps the code
  those tests ran on;
- the `Published/` folder, **moved** to `@ltsName/Published/` by the
  [Publish plugin](../../StandardPlugins/CKli.Publish.Plugin/README.md#publishedfolder--where-the-profiles-live):
  what the default World has published so far is below the cut. The default World is left with an empty folder
  (and its empty `index.json`) until its first publication.

The Stack is committed first (`Before creating Long Term Support world '…'.`: opening the World may have created
its plugin solution), then everything the creation writes is committed by a single `Created Long Term Support
world '…'.` commit, which is **pushed while the locks are still held**: once they are released anybody can publish
again, and the default World must already carry its new `InfVersion` by then. If anything fails before that, both
new folders are deleted and the tracked files of the Stack are restored: nothing is left of the new World locally
(a root branch already pushed stays on its remote: a new attempt accepts it when it is on the same commit).

Then, still under the locks, the default World gets its **initial versions**: without a version in its range, all
its repositories would have a "Missing initial version" issue. In every repository, an empty commit is added on
the root branch (its tip carries the last published version, which a `+fake` must not share) and tagged
`v{cut}+fake` (`v2.0.0+fake` for a cut at `2.0.0-0`); both are pushed. This runs last because a retry must find the
default World as it was: if it fails, the LTS World exists and `ckli issue --fix` (which adds the same empty commit)
then `ckli push` and `ckli tag push` finish the job.

Finally, after the locks are released, the new World is cloned in the Stack's `@ltsName/` folder exactly as
[`world lts clone`](#world-lts-clone-ltsname) does. A failure there doesn't undo anything: the LTS World exists and
`ckli world lts clone <@ltsName>` finishes the job.

### `world lts clone <@ltsName>`
Clones the repositories of an existing LTS World of the current Stack into its `@ltsName/` folder. Use this
when the Stack is already cloned; `clone --lts-name` is the equivalent for a Stack that is not.

Only the missing repositories are cloned, so running it again does nothing. Opening a World for the first
time also generates its plugin solution inside the Stack repository, so the command commits the Stack: no
`push` is needed for the World to exist locally, but a `push --stack-only` publishes that plugin solution.

The World's pinned CKli is then installed as the local tool of its `@ltsName/` folder (idempotent: an existing
manifest is kept and the tool is moved back to the pinned version). Nothing is installed under a test harness,
nor for a World that has no pin (one created by a locally compiled CKli). A failed install is a warning, not a
failure: the next command run with the global `ckli` installs it again.

## World lock commands (lock, unlock)

A **lock is shared by every developer of the Stack**, not by the processes of one machine: it is a Git reference
of the Stack remote, so taking it tells every other clone that this one is busy. It is what serializes an
operation — a publication, typically — across a team.

The two commands are two separate processes, and **nothing is stored locally between them**: the remote
reference is the state, and the lock recognizes its own by the clone that took it. So `world unlock` releases
what a `world lock` of any earlier run acquired, and a second `world lock` from the same clone renews rather
than fails. A second clone of the same Stack — even the same developer's — is a *different* holder.

The name is scoped to the current World (`publish` in the `One` World is `refs/ckli-locks/One-publish`), so two
developers working on two Worlds of one Stack never wait for each other.

**`publish` is taken automatically.** `ckli publish`, `ckli *publish` and `ckli fix publish` hold that very lock
for the whole command and release it when they end (so does [`world lts create`](#world-lts-create-ltsname), with
an `lts` one), so publishing is already serialized across the Stack without anybody running these two commands (`ckli build` takes nothing — it publishes nothing). Their lease is 15 minutes
and is renewed while they work, so a publication is never cut short by its own duration. What the two commands add
is the ability to **reserve** the slot before starting (`ckli world lock publish --duration 30`, which a later
`ckli publish` from the same clone renews rather than trips over) and to **free** one that a crashed run left
behind (`ckli world unlock publish`, rather than waiting the lease out). See
[The publication lock](../../StandardPlugins/CKli.Build.Plugin/README.md#the-publication-lock-one-publisher-at-a-time-per-world).

The `refs/ckli-locks` part is the Stack's
[`LockPrefix`](../README.md#lockprefix-the-reference-namespace-that-locks-the-stack), and **it settles
itself**: the first lock ever taken on a Stack finds out which reference namespace its remote accepts — by
creating and deleting one for real — and records the answer in the default World, which is then pushed. There
is nothing to configure and no setup command to run. Every later lock, on every clone, uses that recorded
value. A host that refuses `refs/ckli-locks` (some refuse anything outside `refs/heads/` and `refs/tags/`)
gets a working fallback automatically; a host that refuses all of them is reported, with each refusal as the
remote worded it.

**A lease expires, and nothing enforces it.** A lock is held for the duration asked for and then frees itself,
so a crashed client stops blocking the team; the flip side is that CKli cannot interrupt whatever the lock was
protecting when the lease runs out. Ask for a duration that covers the work, renew before it ends, or accept
that a colleague may take over. Expiry is decided by comparing the holder's clock to yours, with 30 seconds of
allowance: the lock is only as good as the clocks of the machines using it.

### `world lock <name> --duration <minutes>`
Acquires the named lock of the current World, or renews it when this clone already holds it (whichever run took
it). `--duration` defaults to 5 minutes and cannot exceed 60 — a lock a crashed client holds for longer than
anybody will wait is not a lock. An operation that takes longer asks for its duration, or renews.

A lock held by somebody else is an **error**: the command reports who holds it, since when, and when it frees
itself if its holder does not renew it first. A lease that has been expired for longer than the clock allowance
is taken over, with a warning naming the holder that lost it.

```
> ckli world lock publish --duration 30
Locked 'refs/ckli-locks/One-publish' until 2026-09-21 16:42:07Z (30 minutes).
```

### `world unlock <name>`
Releases the lock when this clone holds it. **This always succeeds**: a lock that is not held, or that already
expired, leaves nothing to do and is not an error, and running it twice is harmless.

A lock held by somebody else is left strictly alone — the command warns, names the holder and releases nothing.
There is no way to force a colleague's lock off: wait for it to expire, or have its holder run `world unlock`.

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

## Plugin commands (info, set, unset, create, add, remove, enable)

The core commands of CKli handles Stack, World and Repo (Git repositories).
The Repo can contain anything. To handle tasks specific to a technology (.NET, Node, Ruby, etc.)
external and optional plugins can be used.

Plugins are written in .NET and distributed as NuGet packages or can be source code directly
in the Stack repository.

### The plugin solution and the CKli version

A World's plugins live in a `{StackName}-Plugins{@ltsName}/` solution inside the Stack repository. CKli
maintains it: `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, the `CKli.Plugins` project
and, when there is one, the `CKli.Testing` reference of `Tests/Plugins.Tests`.

`CKli.Plugins.Core` and every Standard Plugin package are referenced at `$(CKliVersion)` rather than at a
literal version, and that property comes from a **generated, git ignored** `CKli.Version.props` sitting beside
`Directory.Build.props`. So the CKli version each developer runs is *not* recorded in the Stack repository:
two developers on two CKli versions no longer produce a conflicting change in a tracked file. (It used to be a
literal version rewritten on every World open, which left the Stack dirty and made the next `pull` fail.)

`CKli.Version.props` is written on every World open, and rewriting it is what triggers a recompilation of the
plugins. `ckli` passes the value to its own builds explicitly; an IDE or a plain `dotnet build`/`dotnet test`
reads the file. A build that finds no `$(CKliVersion)` at all — a fresh clone where no `ckli` command has run
yet — fails with a message saying so rather than with an obscure NuGet error.

The versions of any **other** plugin package (the ones `plugin add <packageId@version>` installs) stay literal
in `Directory.Packages.props`: those are shared decisions and belong in the Stack repository.

A World that must impose a CKli version carries a `CKliVersion` attribute on the root element of its definition
file instead; CKli then refuses to open it with any other version and builds its plugins against the pinned one.
[`world lts create`](#world-lts-create-ltsname) writes it on the LTS World it creates. A Stack created before
this existed is migrated on its first open: the literal versions become `$(CKliVersion)` and a LTS World's
implicit pin is transferred to the attribute, in one final commit per World.

### `plugin info --skip-pull-stack`

Provides information on installed plugins, their state, Xml configuration element and an optional message
that can be produced by the plugin itself.

`--skip-pull-stack` doesn't update the Stack repository first.

### `plugin set <name> <value>`

Configures a plugin attribute — an attribute of a `<Plugins>` child element of the World definition file —
without editing the XML by hand:

```
ckli plugin set RemoveUselessFakeTag true
```
```xml
<MyWorld>

  <Plugins>
    <VersionTag RemoveUselessFakeTag="true" />
  </Plugins>

  <!-- Folders and Repositories... -->
</MyWorld>
```

The attribute name can be prefixed by the plugin short name (`VersionTag.RemoveUselessFakeTag`). This long
form is required only when more than one plugin supports the same attribute name: the identifier is submitted
to each primary plugin until one of them handles it.

Most of these attributes are booleans: they take the XML `true` or `false` and nothing else (`True`, `1` or `yes`
are errors). A plugin may support another kind of value and validate it itself — `Build`'s `DeleteBeforeBuild`
takes a `;` separated list of paths and refuses an entry that escapes the repository's working folder.

Both sides of this are deliberately **manual**, and it is up to each plugin to implement them:
- the supported attributes are the ones a plugin describes in the message it publishes on the `PluginInfo`
  event — this is what `plugin info` displays;
- writing them is the plugin's `OnPluginSetAsync` override.

The 4 Standard Plugins that currently support attributes are
[`BranchModel`](../../StandardPlugins/CKli.BranchModel.Plugin/README.md#configuration-xml) (`AutoFixUselessBranch`),
[`VersionTag`](../../StandardPlugins/CKli.VersionTag.Plugin/README.md#configuration) (`AutoFixRemovableTag`,
`RemoveUselessFakeTag`),
[`Publish`](../../StandardPlugins/CKli.Publish.Plugin/README.md#configuration) (`KeepLocalReleaseAfterPublish`) and
[`Build`](../../StandardPlugins/CKli.Build.Plugin/README.md#configuration) (`DeleteBeforeBuild`).

As usual, this modification will be "published" when `push` (typically with `--stack-only`) is executed.

### `plugin unset <name>`

Removes the attribute set by `plugin set`: the plugin's own default value applies again. This is not the same
as setting the default value explicitly — the attribute is gone from the definition file.

The `name` accepts the same short and `Plugin.Attribute` long forms.

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
up — see [`CKli.Plugins.Core`'s README](../../CKli.Plugins.Core/README.md#reflectionplugincollectorfactory-reflection-execution-vs-code-generation)
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
