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
  - A `HorizontalContent`'s cells are **columns**: they share the width they are given, so its `MinWidth` is the
    **sum** of theirs. A `FlowContent`'s cells are a **paragraph**: each keeps its own width and the flow breaks
    into as many lines as needed, so its `MinWidth` is the **widest** of theirs (plus its `HangingIndent`, the
    left margin of the lines below the first). An inline list of arbitrary length - a comma separated list of
    repository names - has to be a flow: as a horizontal content its minimal width exceeds any screen past a
    dozen cells, and every cell is then wrapped inside `TextBlock.MinimalWidth` columns. A flow cell is atomic,
    so a group that must not be split (a name and its comma) is one cell.
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

A Stack repository has a single branch and it is `StackRepository.BranchName` (`main`). This is an **invariant,
not a default**: `CheckStackBranchName` refuses a Stack repository that hasn't got it — in `CloneAsync` and in
`TryOpenFromPath`, so a clone cannot slip through and a later command cannot recreate it. Two things it is
deliberately not doing:

- *Working on whatever branch the repository is on.* That cannot be made coherent: the Stack would be read from
  one branch while every reader that names `BranchName` looks at another. `ckli create` also makes `BranchName`
  the remote's **default** branch (`SetDefaultBranchAsync` after the push — a provider may ignore the name given
  to `CreateRepositoryAsync`, GitHub uses the account's), so the two agree.
- *Creating the branch locally.* That is what `FullCheckout` and `EnsureBranch` do when they find neither a local
  nor an `origin/` one, and it gives a branch that tracks nothing — `PushChanges` pushes the head and the head
  must track a remote branch, so such a Stack could never be pushed back.

Anything reading a Stack repository from its remote without cloning it must name `BranchName` rather than let
`GetFileContentAsync` fall back to the remote's default branch: `CKli.Build.Plugin`'s `UpgradeMap.References`
does exactly that for a World Reference's `Published/index.json` and profiles.

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

### `LockPrefix`: the reference namespace that locks the Stack

A Stack is locked by a Git reference in the **Stack repository** — that is what lets developers of one Stack
serialize an operation across all its repositories. The `LockPrefix` attribute of the root element states the
prefix of those references:

```xml
<CK-Build LockPrefix="refs/notes/ckli-locks">
```

**The attribute is the discriminator: present means settled, absent means that nobody has asked for a lock
yet.** There is no "absent therefore default" — the first client that needs a lock determines the prefix and
**records** it, including when the winner is `StackRepository.DefaultLockPrefix` (`refs/ckli-locks`). So
`StackRepository.GetLockPrefix` reads the recorded value (null when there is none) and
`StackRepository.EnsureLockPrefix` is what taking a lock calls: it returns the recorded one, or determines it
now. Nothing else has to be run for a new Stack, and no command exists to do it by hand.

Determining it means asking the repository, for real. `LockPrefixCandidates` are tried in order and each one
is *created and deleted* on the remote (`DistributedLock.ProbeNamespace`). The delete leg is not a formality:
a host that accepts the creation and refuses the deletion would leak a lock on every release, so it
disqualifies the prefix — and the reference the probe just leaked is reported for manual removal. The order is
`refs/ckli-locks`, then `refs/notes/ckli-locks`, then `refs/heads/ckli-locks`: out of `refs/heads/` a lock is
outside the default fetch ref spec, so neither it nor the commits it carries are replicated to every clone,
where the `refs/heads/` fallback is fetched by everyone on every fetch *and* subject to the repository's
branch protection rules — a rule forbidding a non-fast-forward update or a deletion would break the lock
itself.

**A recorded value is never re-probed and never second-guessed**, and before probing anything
`EnsureLockPrefix` fetches and reads the definition file *as the remote has it*: a colleague may have settled
it since this clone was last pulled, and their value wins. That read is a fetch plus a blob read rather than a
pull — the value is only being consulted, and a pull would merge into a working folder that taking a lock has
no business touching (an untracked file the incoming commit also carries is enough to fail the checkout).
If two clients do settle it at the same moment, the loser's push is refused and it is told to pull and retry.

Whatever the prefix, its last part is always `ckli-locks` (`StackRepository.LockSegmentName`), and
`StackRepository.IsValidLockPrefix` enforces it. Every lock reference of every Stack therefore carries that
one segment, which is what makes a client locking under *another* prefix detectable: one reference name
segment to look for in the remote's advertised references, instead of a list of candidate prefixes to keep in
sync.

**Only the default World can carry the attribute.** It is a **Stack** level setting: every World of a Stack
locks in the one Stack repository, so whether a reference name is accepted there is the same answer for all
of them, and the prefix is read from the default World's file whatever the current World is. A copy on a LTS
World could only diverge from the one that is used, so `WorldDefinitionFile.Create` refuses it — the world
does not load — and `ckli world lts create` strips it from the definition file it derives.
`WorldDefinitionFile.SetLockPrefix` enforces the same rule when writing.

An invalid value is refused the same way, by an exception that prevents the world from loading rather than a
fallback to the default: the lock prefix is precisely what every client must agree on, so a Stack that states
an unusable one must be fixed, not worked around by a client that then locks somewhere else than its
colleagues do.

The mutex itself is
[`GitRepository.DistributedLock`](#gitrepositorydistributedlock-a-mutex-shared-by-the-developers-of-a-stack).

### `MinCKliVersion` and `CKliVersion`: the CKli versions a World accepts

Two optional attributes of the root element, both read by `LocalWorldName.DoLoadDefinitionFile` **before
anything else** - even before the root element name is validated - because a CKli that cannot handle this
World must say so rather than fail later on something incomprehensible. Neither has an API: they are written
by code or by hand.

- `MinCKliVersion` is a **lower bound**. A CKli older than it refuses to open the World and says to run
  `ckli update`. This is the guard for a definition file an older CKli could not even parse.
- `CKliVersion` is an **exact pin**, exposed as `WorldDefinitionFile.PinnedCKliVersion`. A CKli that is not
  that version refuses to open the World, and the plugin solution is built against the pinned version rather
  than the running one (`PluginMachinery.EffectiveCKliVersion`).

The pin is what keeps a **LTS World frozen**: it is written by `World.CreateLTSAsync` (`ckli world lts create`)
and the default World deliberately carries none - there, each developer keeps its own CKli version, which is
what [the plugin solution's `$(CKliVersion)`](#plugin-discovery-and-loading) makes possible.

Both skip their check when the running CKli is `SVersion.ZeroVersion` (`0.0.0-0`, a locally compiled one):
a developer building CKli must be able to open any World. For the same reason `CreateLTSAsync` refuses to
*write* a `0.0.0-0` pin - nobody can install that version, so the World would be unopenable by everyone else
and only a manual edit could repair it.

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
second world is *added* to that clone through the same `CKliLTSClone.AddWorld` that `ckli world lts clone` uses, and
its own references are followed too. `CKliClone.CloneState` is what makes this terminate: it keys the handled
set on `(url, LTSName)` — not on the url alone — and remembers the `StackRoot` of the Stacks that this command
cloned. A Stack found already cloned *elsewhere* on the machine is never touched: that folder is not this
command's to modify, so the `world lts clone` command line to run there is reported instead.

### Opening a world doesn't require its folder

`World.Create` only parses the path and reads the xml definition file, so `StackRepository.OpenWorld` can open
a world that has never been cloned — and `World.FixLayout` then clones its missing repositories. That is the
whole of `ckli world lts clone`: create the folder, open the world, fix the layout. Two consequences that are easy
to miss:

- **A world's physical layout excludes the other worlds' roots.** `ReadPhysicalLayout` skips them explicitly.
  Without it the default world (rooted at the StackRoot) sees an LTS world's repositories as misplaced copies
  of its own and `layout fix` *moves* them out of it — `layout xif` adopts them, and `issue --fix` does the
  move with `deleteAliens: true`.
- **Opening a world writes into the Stack repository**: `PluginMachinery` creates its
  `{LTSName}/{StackName}-Plugins{LTSName}/` solution there, which is tracked content. `StackRepository.Close`
  only commits a dirty definition file, so whoever adds a world commits the Stack itself.
  `StackRepository.GeneratedFileIgnorePatterns` are deliberately unanchored for the same reason: the previous
  `/CKli-Plugins/CKli.Plugins/CKli.CompiledPlugins.cs` matched only the default world of a Stack literally
  named "CKli". `EnsureGeneratedFilesIgnored` repairs older Stacks - it is called when a world is added and
  whenever the plugin machinery writes the generated `CKli.Version.props`.

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
    string? TryGetRequiredSecret(IActivityMonitor monitor, IEnumerable<string> keys, LogLevel level = LogLevel.Error);
}
```

The keys are ordered from the strongest to the weakest and the first one that resolves wins — which is how a read
falls back to the write PAT. When none resolves, the store logs the `dotnet user-secrets set` line to run.

The default implementation (`DotNetUserSecretsStore`) uses the standard .NET user secrets mechanism, with
`CKliRootEnv.InstanceName` as the secrets id (`CKli` for a regular run).

PAT key names follow the convention `{Prefix}_READ_PAT` / `{Prefix}_WRITE_PAT`, and the `{Prefix}` is derived from
the origin url by `GitRepositoryAccessKey.FindOrCreate` — not by the `GitHostingProvider`, which is itself selected
from that same url. It is the provider name plus the url's first path part (the owner), uppercased, with every
character outside `A-Z0-9_` replaced by `_`: `https://github.com/CK-Build/CKli` gives `GITHUB_CK_BUILD`, hence
`GITHUB_CK_BUILD_READ_PAT`. A `file://` url is the exception: its key is the bare `FILESYSTEM_GIT` with no suffix
(`IsPublic` is null, so read and write name the same key).

Access keys are **interned in a process wide cache** keyed by `(ISecretsStore, PrefixPAT)` — the store is part of the
key because a key caches the credentials it resolved from the store it was created with, so sharing by prefix alone
would silently bind every later key of a prefix to the first caller's store.

That cache, the credentials cached on a key, and `DotNetUserSecretsStore`'s parsed document are each **guarded by a
lock**, because every parallel command resolves them concurrently: `ckli fetch`, `ckli pull`, the clone path and
`ckli deps update` walk the World through an `ActivityMonitorAsyncPool`, and each repository's fetch reads its
`AccessKey` and asks it for read credentials. Unsynchronized, one shared prefix — a World is usually one owner — meant
every one of those threads racing on a single `Dictionary` entry. Everything the lock protects is resolved once per
prefix and then cached, so the cost is a startup blip on an operation that is about to hit the network, and it is also
what makes the store be asked once rather than once per concurrent repository (hence one `dotnet user-secrets set`
message instead of one per repository).

The [root README](../README.md#private--public-stack-and-repositories) documents this from the user's side.

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

### `GitRepository.DistributedLock`: a mutex shared by the developers of a Stack

`StackRepository.GetLock( monitor, lockName, out var theLock )` gives a mutex backed by a Git reference of the
Stack remote — `{LockPrefix}/{lockName}`. It is how an operation is serialized across developers rather than
across the threads of one process.

**The commit chain is the algorithm.** The reference points to a chain of lease commits, each one a child of
the one it replaces, so acquiring, stealing an expired lease and renewing are all **fast-forward** updates —
and a fast-forward is the only conditional update the Git protocol offers. libgit2 compares the tip advertised
during the push's own negotiation with the commit being pushed, and the receiving end checks the old object id
before moving the reference. Two clients cannot both win: the loser gets `NonFastForwardException`, which the
lock reports as `AcquireResult.Held`.

A lease commit unrelated to the current one could only be pushed with `+`, and force is precisely the *absence*
of a condition — that is the whole reason for the chain, and the reason `Release` **deletes** the reference
rather than resetting it (a deletion is what keeps the chain from growing forever).

Four things that are not obvious:

- **The remote is read through a fetch, never from the advertised object id alone.** A colleague's lease is a
  commit this clone does not have: reading it, and building the child that replaces it, both need the object.
  `TryRead` lists the references (one round trip, which also carries the prefix check below) and then fetches.
- **A deletion is not conditional on our own commit** — it removes whatever the reference points to. `Release`
  is safe because a lease that has expired does not delete at all, and a lease that has not expired cannot
  legally have been stolen. A lease that lost the reference refuses to release and says who holds it.
- **`ClockSkewAllowance` is load-bearing.** A lease expires on the writer's clock and is read on the stealer's,
  and nothing in the protocol provides a shared time. An expired lease becomes stealable only once it has been
  expired for that long. This mutex is only as good as the clocks of the machines using it.
- **Expiry is not enforced.** Nothing here can interrupt a caller, so `Lease.IsExpired` must be tested before
  any step that must not run concurrently, and `Lease.Token` is the fencing token if the protected resource can
  honor one. `Lease` is `IDisposable` only to warn about a path that forgot to `Release` — disposing does not
  release, because a release is a remote call that needs a monitor and can fail.

**`OwnerId` identifies a developer, a machine and a clone** — `Name <email> on MACHINE (C:/Dev2/CKli)`, from the
Stack repository's configured Git identity — and it is one string because it plays both roles: it is what a
lease displays when it blocks somebody, and it is what ownership is compared on (ignoring case, since the clone
path is in it and Windows does not distinguish two spellings of one folder).

That identity is what makes the lock survive the process that took it. `ckli world lock` and `ckli world unlock`
are two separate runs, so the lease cannot be an object — and is deliberately not a local file either: the
remote reference is the state, and a later run recognizes its own lock by that id. Hence the two API shapes the
commands use: `AcquireOrRenew` (renews a lease of this `OwnerId` where `TryAcquire` would report it held) and
`Unlock` (releases by owner, needing no `Lease`). The clone is part of the id on purpose — two clones of one
Stack, even one developer's, publish independently and are therefore two holders.

`Push` deliberately bypasses `GitRepository.Push`: that method merges the `DeferredPushRefSpecs` into every
push and clears them on success (which a lock, pushed repeatedly while a command prepares its own references,
must not do) and reports every rejection as a plain failure, where a lock must tell a lost race from a remote
that refuses the reference name. When a rejection comes back through `OnPushStatusError` rather than as a
`NonFastForwardException`, the reference is re-read to tell the two apart: one that moved is a lost race, one
that did not is a host that will never accept this prefix — which is what points the user at `LockPrefix`.

Finally, every lock reference carries the `ckli-locks` segment whatever its prefix, and the listing checks for
one that is *not* under this client's prefix. Mutual exclusion holds only while every client agrees on the
prefix; two that disagree would both acquire and neither would see the other. That is an error, never a
warning — it is the one place where the failure is visible at all. See
[`LockPrefix`](#lockprefix-the-reference-namespace-that-locks-the-stack).

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

It answers a `(bool Success, byte[]? Content)`, the same shape as `GetReleaseAsync` — the two share the
convention that *nothing to read* is a legitimate answer and not a failure, and that `notFoundLogLevel`
decides whether it is said out loud (`Trace` by default), so a caller for which the absence matters gets it
reported without having to say anything itself:

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

### Plugin attributes (`ckli plugin set` / `ckli plugin unset`)

An attribute of the plugin's global configuration element can be set from the command line rather than by editing the World
definition file. Both ends of this are **manual** — CKli has no schema of what a plugin's configuration accepts:

- The supported attributes are the ones the plugin describes in the message it publishes on the `WorldEvents.PluginInfo`
  event: that is what `ckli plugin info` displays.
- Writing one is the plugin's own `OnPluginSetAsync( monitor, pluginInfo, attributeName, attributeValue )` override
  (`PrimaryPluginBase` and `PrimaryRepoPlugin<T>` both offer it, it defaults to "not handled").

The command submits the attribute name to each primary plugin until one answers a non null result: `true` (handled),
`false` (error). `null` means "not mine" and the submission continues, so the `Plugin.Attribute` long form
(`VersionTag.RemoveUselessFakeTag`) is needed only to disambiguate a name that more than one plugin supports — when it is
used, the plugin receives its own `PluginInfo` instead of a null one and knows it was explicitly targeted.

An override writes through `PrimaryPluginContext.Configuration`, typically with `SetBooleanAttribute` (which rejects
anything but the XML `true` and `false`) or `SetAttribute`; a null `attributeValue` — the `plugin unset` command — removes
the attribute, so the plugin's default applies again:

```csharp
protected override Task<bool?> OnPluginSetAsync( IActivityMonitor monitor,
                                                 PluginInfo? pluginInfo,
                                                 string attributeName,
                                                 string? attributeValue )
{
    bool? result = null;
    if( attributeName.Equals( XNames.RemoveUselessFakeTag.LocalName, StringComparison.OrdinalIgnoreCase ) )
    {
        result = PrimaryPluginContext.Configuration.SetBooleanAttribute( monitor, XNames.RemoveUselessFakeTag, attributeValue );
    }
    return Task.FromResult( result );
}
```

The `StackRepository.Close` that ends the command saves and commits the modified definition file.

## Plugin discovery and loading

The [`PluginMachinery`](Plugin/Impl/PluginMachinery.cs) orchestrates:
1. Discovery of plugin projects in `<WorldName>-Plugins/` inside the Stack.
2. Code generation and Compilation via `IPluginFactory` (reflection-based `None` mode, or `Debug`/`Release` compiled code generation).
3. Loading into a collectible `AssemblyLoadContext` (via `CKli.Loader`).

There's nothing simple here. An important part of the magics lies in the the **CKli.Loader** and the **CKli.Plugins.Core** assemblies and
how they are used by the `<WorldName>-Plugins` solution.

### `$(CKliVersion)`: the CKli version is not in the Stack repository

`CKli.Plugins.Core` and the Standard Plugins are referenced at `$(CKliVersion)` in the solution's
`Directory.Packages.props`, never at a literal version. The property comes from `CKli.Version.props`
(`PluginMachinery.CKliVersionPropsFileName`), a **generated and git ignored** file written beside
`Directory.Build.props` on every World open by `EnsureCKliVersionProps`.

This is what lets two developers run two CKli versions on one Stack. The literal version it replaces was
rewritten on every World open, so it recorded whoever ran `ckli` last: the Stack working folder was left
dirty and the colleague's next `ckli pull` failed on a merge it could not perform.

Consequences worth knowing:

- **Writing that file is the recompilation trigger.** It is the stamp of the version the plugins were last
  built against, so `EnsureCKliVersionProps` reports `mustRecompile` when it changes. That matters because
  `PluginCollectorContext.ComputeSignature` hashes `World.CKliVersion`, so a stale generated
  `CKli.CompiledPlugins.cs` is detected - but in `PluginCompileMode.None` there is no such signature and a
  stale dll built against another `CKli.Plugins.Core` would only fail at plugin instantiation.
- **`DoCompilePlugins` passes `-p:CKliVersion=` as a global property**, which wins over the file: CKli's own
  builds never depend on it being up to date. An IDE or a plain `dotnet build`/`dotnet test` reads the file,
  and a build that finds no value at all fails on an explicit `<Error>` rather than an obscure NuGet message.
- **`Tests/Plugins.Tests` has its own `Directory.Build.props`** (it works around
  [dotnet/sdk#45953](https://github.com/dotnet/sdk/issues/45953)) and MSBuild stops walking up at the first
  one it finds, so the property defined beside the solution does **not** reach that project: its companion
  imports the generated file itself, with its own relative path.
- **Only the CKli owned references use the property.** The versions `ckli plugin add <packageId@version>`
  installs stay literal in `Directory.Packages.props`: those are shared decisions and belong in the Stack.
- **The migration is one-time and self-healing.** `MigrateToCKliVersionProperty` converts a solution created
  before the property existed. It must add the `<Import>` to `Directory.Build.props` **and** rewrite the
  versions - doing only the latter would leave `$(CKliVersion)` undefined and nothing would restore. For a
  LTS World it also transfers the implicit pin the literal version used to carry to the definition file's
  `CKliVersion` attribute, so the migration loses nothing. It is skipped for the `CKli` Stack itself, whose
  plugin solution uses project references and has no `Directory.Packages.props`.

# Commands

The static `CKliCommands` (in the `CKli` namespace) registers all intrinsic commands and is the CKli entry point: it executes the `Command` (abstract)
that describes and implements a CKli command:

```csharp
public abstract class Command : CommandNamespaceItem
{
    protected Command( string commandPath, string description, ... ) { ... }

    // CommandPath and Description come from CommandNamespaceItem.
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

## Command namespaces

A `CommandNamespace` is a dictionary of `CommandNamespaceItem` indexed by path. An item that is a `Command` is executable;
an item that is not is a **pure namespace** — `"world reference"` exists because `"world reference list"` does, and
`CommandNamespaceBuilder` declares such parents automatically.

A pure namespace is owned by nobody: `"fix"` is populated by `CKli.Build.Plugin` and `CKli.HotZone.Plugin`,
`"maintenance"` by `CKli.Build.Plugin` and `CKli.Migration.Plugin`. Its description is consequently a list of
`CommandNamespaceItem.DescriptionPart` (`Text`, `Origin`, `HelpUrl`) that are **appended**: any number of plugins can
describe the same namespace with `[CommandNamespace( path, description, HelpUrl = "..." )]` on the plugin class, and
each contribution keeps its own link to an external documentation. `CommandNamespaceBuilder.Build( monitor )` orders
the parts on their `Origin` plugin name — the intrinsic CKli ones (a null `Origin`) first — so that the plugin
activation order cannot leak into the rendered help. `Description` joins them one blank line apart: they are
paragraphs written by different people, not one text.

The intrinsic namespaces are described by `CKliCommands` itself. Describing a namespace that no command populates is
**warned and ignored**, not refused: a plugin may legitimately describe a namespace that a currently disabled plugin fills.
A namespace with no description at all is valid — it is up to the help renderer to display the child commands instead.

## The two help modes

`ckli --help` used to list all 55 commands with every option and flag: 382 lines, of which the options and flags alone
were 181. It is now **collapsed**: one line per depth 1 item, plus one row of child names below each namespace so that
the map still names every command. 43 lines.

The rule is simply **collapsed if and only if no help path was given**. Any `--help` that names something — a namespace
(`ckli tag --help`) or a command (`ckli tag push --help`) — displays that whole subtree in full, exactly as before;
the largest namespace of this stack is 58 lines, so there is nothing to collapse there.

What the collapsed line shows is `CommandNamespaceItem.Summary`: the **authored** `Summary` of each description part
(`[Description( "…", Summary = "…" )]`, `[CommandNamespace( …, Summary = "…" )]`, or the `summary:` argument of a
`Command` constructor — it is the last parameter and defaults to null, so no existing command had to change). When a
part has none, its whole `Text` is used with its line breaks collapsed: nothing is ever dropped, the line is just longer
until someone writes a summary. Deliberately **not** the first line of the description — most of them are prose whose
first line stops mid-sentence.

Ordering is `CommandNamespaceItem.PathComparer`: the path without its `*` markers, then the unmarked one first, so
`build` precedes `*build` and `publish` precedes `*publish`.

The global options and flags are condensed to a single line in the collapsed help. `ckli --help --global` details them:
`--help` may be followed by a trailing run of help modifiers, which is the only place `--global` is recognized.

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
                                [Description( "Build regular exploratory, prerelease or stable versions instead of CI versions." )]
                                [OptionName( "--release" )]
                                bool release = false,
                                [Description( "Build a ci.0 version when a released version is already available on the commit." )]
                                [OptionName( "--ci.0" )]
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


