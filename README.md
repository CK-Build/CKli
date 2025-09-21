# CKli

CKli is a tool for <u>multi-repositories</u> stacks.
It allows to automate actions (build, package upgrade, etc...), on <u>Worlds</u> (a group of repositories),
and concentrates informations in a single place.

:warning: This is currently under development.

## Getting Started

### Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download)

### Installation (not supported yet)

CKli is a [dotnet tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools).
You can install it globally by running:

```powershell
#latest stable
dotnet tool install CKli -g
```
**This is the theory.**
In practice a Long Term Support World should use a locked version of CKli and its plugins.
The idea was to, in such case, install a **local** dotnet tool in the World and use the "fact"
that the locally installed tool would take precedence over the global one. Simple and effective.

Unfortunately this doesn't seem that simple:
https://github.com/dotnet/sdk/issues/14626
See also https://github.com/dotnet/sdk/issues/11958


### Run CKli

If you installed CKli globally, you can run `ckli` in any command prompt to start it.

## The basics: Stack-World-Repo

:warning: create a Demo-Stack. Idea: use an external OSS well-known project.

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

### `clone <url> --private --allow-duplicate`
Clones a Stack and all its current World repositories in the current directory.

`--private` drives the name of the Stack repository folder: it is `.PrivateStack/` 
instead of `.PublicStack/`.

`--allow-duplicate` must be specified if the same Stack has already been cloned
and is available on the local system. In such case, the Stack's folder name will be
`Duplicate-Of-XXX/` instead of `XXX/`.

### `pull -all --skip-pull-stack`
Resynchronizes the current Repo or World from the remotes.

When the current directory is in a Repo, only the current repository is pulled
unless `--all` is specified.

When `--skip-pull-stack` is specified, the Stack repository is not updated.

### `fetch -all --from-all-remotes`
Fetches all branches of the current Repo or all the Repos of the current World.

When the current directory is in a Repo, only the current repository's branches are
fetched unless `--all` is specified.

`--from-all-remotes` fetches from all remotes instead of the 'origin' remote.

### `push -all --stack-only --continue-on-error`
Pushes the current Repo or all the current World's Repos current branches to their remotes.
Any conflict is an error, unless `--continue-on-error` is specified, the first error stops
the push.

When the current directory is in a Repo, only the current Repo's current branch is
pushed unless `--all` is specified.

When `--stack-only` is specified, only the Stack repository is pushed. Current Repo and
World is ignored.

### `repo add <url> --allow-lts`
Adds a new repository to the current world.

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to add a new Repo to a Long Term Support World.

This clones the repository in the current directory, updates the World's definition file in
the Stack repsoitory and creates a commit. To publish this addition, a `push` (typically
with `--stack-only`) must be executed.

### `repo remove <name or url> --allow-lts`
Removes an existing Repo from the current world.

If the current World is a LTS one (`CK@Net8`), `--allow-lts` must be specified because
it is weird to remove a Repo from a Long Term Support World.

This deletes the local repository, updates the World's definition file in
the Stack repsoitory and creates a commit. To publish this removal, a `push` (typically
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

## Ckli status

Whether the current directory is in a Repo or not, the "status" is quite different.
When we consider the whole dependency graph from a Repo we should handle 2 sides:
- The Imported, ingress, predecessors from which the Repo imports/consumes its dependencies.
- The Exported, egress, successors to which the Repo exports/provides its published packages.

This forms 2 "cones" that should drive the UX.
1. Ingress: "As a developper, I want to update any packages in this Repo."
2. Egress: "As a developper, I want to propagate the changes I made to this Repo to all the Repo that uses it."

The "release of a Repo" is 1) followed by 2). This may touch only a few Repo in the World... or a lot.
Before the ultimate `ckli update` that would do all the magics, there's some work to do.

A question that seems easy to answer to is: "Is this Repo up-to-date?".
First, the good question is "Is this branch in this Repo up-to-date?". So what does it mean?

- Ingress

Are there new versions of the packages this Repo uses?
If yes, then I upgrade them, test this Repo and we are good. No. This is true for external packages, that
are not produced by this World, but in the World, some Repo may not be "up-to-date". But... wait!
Is it my responsibility to produce packages from Repo because I want this Repo to be up-to-date? Moreover I
don't necessarily master this low-level Repo... Well, it depends.

When I'm working at the World level, yes (refactoring scenario). If my focus is solely this Repo (like in a
bug fix scenario), then no. But when I'm fixing stuff, I should not upgrade any package (unless the issue
comes from a dependency). And IF I upgrade a dependency, I should ensure that all other Repos in my World
that depend on the upgraded package(s) also use the upgraded version.

:warning: In a World, all external packages SHOULD use the same version. This is not always the case, this
rule can be temporarily broken (hot-fix), but this is a problematic state that must eventually be handled.





- Egress
Very easy: the commit point is clean.
1. My working folder is not dirty.
  - I have no uncomitted modified files, no staged files, the current commit contains all the code.
2. If the branch is tracked, I must check if the remote has no new commits (from my colleagues). If yes,
   I may need to `pull` (and possibly merge my local changes).
3. The tests pass.








1. Git level:
  - `GitRepositoryInfo.SimpleStatusInfo`
    - CurrentBranchName:  string
    - IsDirty: bool
    - CommitAhead int?
    - CommitBehind int?
2. Solution level (can be techno agnostic but there's the notion of "Solution")
  - BasicDotNetIssue: enum { None, DirtyFolder, MissingProjects, MultipleSolution, BadNameSolution, EmptySolution }
  - AllProjects: List of NormalizedPath
  - Issue must be None to continue.
3. RawDependency level (may be techno agnostic... but versions come into play)
  - PackageRegerences: List of (PackageId, Version)
  - PublishedProjects: List of NormalizedPath
  - SupportProjects: List of NormalizedPath
4. SimpleGitVersion level
  - The RepositoryInfo should contain everything needed.
5. "Imported/igress/predecessors"

