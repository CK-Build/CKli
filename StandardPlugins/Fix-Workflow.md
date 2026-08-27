# The Fix Workflow

## What it's for

A stable release ships, and later a bug is found in it — but the World has moved on: a newer Major or
Minor has since become the current hot line. Rebuilding and republishing everything just to patch an
old, already-superseded release would be wasteful and risky: it would drag the fix through every change
that has happened on the hot line since, none of which the fix needs or should depend on.

The Fix Workflow exists for exactly this case: producing a Patch release for a version that sits
*behind* the current hot zone, without touching or depending on anything that has changed since. It is
deliberately walled off from the regular workflow (see `HotZone-Workflow.md`): starting a fix on a
version that is still inside the hot zone is refused outright, with a pointer back to the regular
`build`/`publish` commands instead — the two workflows are mutually exclusive by design, not two ways
to do the same thing.

## The commands

- `fix start <version> [--move-branch] [--with-empty-commit]` — begins (or restarts) a fix for a given
  Major or Major.Minor version.
- `fix info` — displays the current fix workflow, if any.
- `fix cancel` — discards local, unpublished fix artifacts and abandons the current workflow.
- `fix build [--ci] [--skip-tests] [--force-tests] [--rebuild]` — builds every target without
  publishing.
- `fix publish [--ci] [--keep-branch] [--rebuild]` — builds and publishes; on success, closes the
  workflow.

## How it works

### Starting: computing the ordered list of targets

`fix start` is given the version to fix (say `v1.2.3`); the actual target produced will be its next
Patch (`v1.2.4`). This becomes the *origin* of the fix.

From there, the workflow discovers every repository that is *impacted*: any repository whose own last
release, for each of its Major.Minor lines, consumed exactly the version being fixed. Each impacted
repository becomes a target in its turn, and its own consumers are examined the same way, recursively —
a fix can ripple arbitrarily far downstream, and arbitrarily far back through Major.Minor history, since
nothing here is scoped to "the current hot zone." The resulting targets are ordered so that a producer
always appears before its consumers, exactly like the regular workflow's build order.

For each target, a `fix/vMajor.Minor` branch is located or created, anchored at the exact commit being
fixed — moved onto it if requested, or given an empty starting commit to carry the fix's changes.

The complete, ordered list of targets is the *Fix Workflow* itself: an immutable value, persisted
locally (outside of Git, tied to the current World) so that the operation can span multiple sessions —
start it, work on the fix across one or more of the `fix/` branches over however long it takes, then
either build/publish it or cancel it. Other plugins can react to a workflow being started before it is
persisted, to prepare or validate the commits on each target branch.

### Building: two invariants kept deliberately narrow

A Fix Workflow enforces two guarantees that are stricter, and different in kind, from the regular
workflow's branch-compatibility invariant:

1. **The set of packages a fixed repository produces cannot change.** Adding or removing a produced
   package is an architectural change, not a fix, and is rejected outright.
2. **Every consumed package must stay at exactly the version it was at in the original release** — with
   two exceptions: packages that live outside the World entirely (an external NuGet dependency, say),
   and packages produced by *other targets of this same fix workflow*, which are deliberately allowed to
   move as the fix is built target by target, each one's freshly-fixed version becoming visible to the
   targets that depend on it.

In other words: a fix is isolated from everything that has happened on the hot line since the original
release. The only things allowed to shift are the world outside the World, and the other repositories
being fixed together in this exact operation. This is why the workflow doesn't need — and deliberately
avoids — the branch-resolution machinery of the regular workflow: there is no "closest branch," no
"same or ancestor branch" reasoning here, because a fix's dependency versions are pinned by definition
rather than resolved.

Targets build one at a time, in the order established at `fix start`, each on its own `fix/` branch.
The whole operation stops at the first failing target, leaving whatever already succeeded untouched
until the workflow is retried or cancelled.

### Publishing

`fix build` only builds; `fix publish` builds and then publishes every target — pushing tags, packages,
and hosted releases, and pushing the `fix/` branch itself. On a successful non-CI publish, the `fix/`
branches are deleted by default (the fix has fully landed, nothing more to track) and the persisted
workflow is discarded, closing the operation. `--keep-branch` opts out of the branch cleanup. A CI
publish always keeps its branches — a fix's CI line is meant to be iterated on, not thrown away after
one build.

### Cancelling

`fix cancel` destroys any local, unpublished build artifacts for every target and discards the
persisted workflow. It is a full abort, safe at any point before a `fix publish` has completed
successfully.

## How this relates to the regular (HotZone) workflow

The two workflows partition the World's history: the regular workflow (`HotZone-Workflow.md`) owns
"the current hot zone" — whatever is being actively built toward, propagating change transitively
across the World. The Fix Workflow owns everything *behind* it — a single already-superseded release
line, explicitly isolated from the hot zone's ongoing churn. A version can only ever be handled by one
of the two: `fix start` refuses a version that is still in the hot zone, precisely to keep that
boundary intact.
