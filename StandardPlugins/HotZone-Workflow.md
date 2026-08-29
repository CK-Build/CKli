# The HotZone Workflow

## What it's for

This is CKli's regular, day-to-day build/publish workflow (`build`, `publish`, `*build`, `*publish`):
the one that produces the "current hot zone" — whatever version, on whatever branch, is actively being
built. Given a target branch, it walks the World's entire dependency graph, works out which
repositories actually need to be rebuilt because something they depend on changed, builds them in the
right order, and publishes the result.

Everything this workflow touches is *live*: a change to one repository can ripple forward through every
repository that depends on it, transitively, within the same operation. That reach is exactly what
makes the workflow's central correctness question worth stating precisely — and worth proving.

For patching an already-superseded release without disturbing any of this, see `Fix-Workflow.md`
instead: the two workflows are deliberately kept separate.

## `stable`, `dev/stable`, and the trunk workflow

Every branch CKli manages comes as a pair: a base branch (`stable`, at the root) and a working branch
prefixed with `dev/` (`dev/stable`). The split is deliberate, and stated plainly in the code itself:
development always takes place in the `dev/` branch — the base branch never receives a direct commit.
Every day-to-day commit a developer makes lands on `dev/stable`. `stable` itself only ever moves
through one specific, controlled operation: *integrating* `dev/stable`'s content into it — which, on
success, also removes `dev/stable`, since it has nothing left to represent once its content is `stable`'s.

This is a form of trunk-based development: `stable` is the trunk, always in a releasable state, every
one of its commits corresponding to something CKli actually built and tagged, while `dev/stable` is the
single, short-lived integration branch that continuously absorbs work until it's folded back into the
trunk. Rather than long-lived feature branches drifting away from `stable` before an eventual, risky
merge, there is exactly one place work accumulates per branch, reconciled with the trunk as part of the
very build/publish operation that produces a release — a successful non-CI publish is precisely what
deletes the now-empty remote `dev/stable`.


## The commands `build` vs. `*build`: how far upstream one operation reaches

Every build command targets a set of *pivots* — the current repositories by default, or every
repository in the World with `--all`. Downstream of the pivots, propagation is always unconditional:
consumers are always read from their `dev/` branch, and the pivots' effect on them is always
considered, no matter which command is used.

Upstream of the pivots is where `build`/`publish` and `*build`/`*publish` genuinely differ. Plain
`build`/`publish` reads everything the pivots depend on from its *regular* branch — only what has
already been published counts; unpublished work still sitting on an upstream repository's own `dev/`
branch is invisible to the operation. `*build`/`*publish` — CKli's "upstream closure" commands — read
those same upstream repositories from their `dev/` branch too, so unpublished upstream work becomes
visible and can pull that repository, and everything between it and the pivots, into the same build.

This changes how far a single operation reaches, not what it's allowed to produce. Whichever
repositories actually end up in the graph, the demonstration below holds for all of them: running plain
`build` when `*build` was really needed just means an operation that should have included an upstream
repository doesn't — not a version that violates the invariant. Keep this in mind reading mechanism (2)
below: it establishes that rebuilding propagates to every consumer *within whatever graph the command
constructed* — `build` vs. `*build` is what decides how far that graph reaches upstream in the first
place.

## The Branch Model: "stable is cool"

`stable` is only the root of a whole namespace of such pairs. A World can configure a chain of
additional, increasingly provisional branches above it, plus any number of ad hoc exploratory branches
alongside them. Together they form the *branch namespace*: the full set of named branches a World
recognizes, each one paired with its own `dev/` branch exactly as `stable` is.

### The main line: a NATO-alphabet prerelease chain

Beyond `stable`, a World can configure a chain of prerelease tiers named after the NATO phonetic
alphabet — `alpha`, `bravo`, `charlie`, … up to `zulu` — the 26 names CSemVer defines as conformant
prerelease identifiers, corresponding to an increasing quality: `alpha` is the first and weakest of
them, `zulu` the last and strongest, right before `stable` itself. A World's configuration lists
whichever subset of these tiers it wants, ordered by *decreasing* quality: the tier closest to `zulu`
sits closest to `stable`, and each one further down the alphabet is a child of the one configured just
before it. A typical simple branch model is:
```
stable > romeo > papa > alpha
```

That ordering directly builds the branch hierarchy, by simple succession: the first tier configured
sits directly below `stable`, and each tier configured after it becomes a child of the one just before
it.

### Exploratory branches: `explo/`

Alongside the main line, a World can also define `explo/` branches: short-lived, ad hoc branches for
things like A/B-testing two approaches side by side, with no ordering implied between them. Unlike the
main line's single chain, exploratory branches attach to an explicitly named parent — the root, a
main-line tier, or another exploratory branch — and can nest arbitrarily deep, forming a tree rather
than a line. Two exploratory branches sharing the same parent are genuine siblings: neither is an
ancestor of the other, and a package built on one cannot depend on a package built on the other.

## The core correctness invariant

For any package `A` produced by CKli, and any package `B` that `A` depends on (a `<PackageReference>`
to a package produced by another repository in the same World):

> `Version(A).BranchName` must be the same as, or a child of, `Version(B).BranchName`.

Branches form a chain down to `stable`, exactly as described in *The Branch Model* above: `stable` is
the root, each configured main-line tier is a child of the tier configured immediately before it, and
exploratory branches attach the same way to whichever parent they were explicitly given. A branch
further from `stable` is "hotter" — more specific, more provisional. The invariant says: **a package
can only depend on a package produced on the same branch, or on a "cooler" (more stable, ancestor)
branch than its own.** It can never depend on something produced on a hotter branch than itself.

Concretely, take a World whose main line is `stable > zulu > alpha`:
- an `-alpha` package can depend on `-alpha`, `-zulu` or `stable` packages.
- a `-zulu` package can depend on `-zulu` or `stable` packages but never on a `-alpha` one.
  (And neither can depend on any exploratory branch).

This matters because a "hot" build is provisional by nature (it may be abandoned, rebased, or simply
never promoted to `stable`), while what it depends on must be at least as trustworthy as what will
eventually consume it. Allowing the reverse — a stable package quietly depending on a `-alpha` build —
would mean a supposedly-stable release's behavior depends on unreleased, may-never-ship code.

## The demonstration

Nothing enforces this invariant with a single obvious guard clause. It falls out of three separate
mechanisms in the build pipeline, each independently unremarkable, whose combination guarantees it.

### 1. Every version produced by one build pass carries the same branch stamp

A single build/publish operation targets exactly one branch — call it the *target branch*. Every
package produced during that operation, whatever repository it comes from, has its version's branch
name set to that same target branch. Nothing that builds in that pass can produce a version on any
other branch.

### 2. Rebuilding is transitive to every consumer, within the same pass

When a repository's upstream dependency is rebuilt during a build pass, that repository is *also*
pulled into the same pass and rebuilt — even if its own repository doesn't have a matching branch yet
(the branch is created as part of the build). This propagation is transitive and walks the *entire*
dependency graph of the World, not just the repositories explicitly requested: a consumer can't be
"out of scope" and thereby escape being rebuilt when something it depends on changes.

Combined with (1): if an upstream `B` is rebuilt for the target branch, then every direct or indirect
consumer `A` of `B` is rebuilt in the very same pass — so `A` and `B` end up on the *same* branch.
That trivially satisfies the invariant (same branch, no ancestor relationship needed).

### 2bis. When a dependency isn't rebuilt, it's because nothing changed to require it

The one gap left by (2) is a consumer that references an upstream that *isn't* rebuilt in the current
pass — has its reference to that upstream become stale without anyone re-checking it? No: a dedicated
check compares what every non-rebuilt solution currently references against the upstream's last
*published* version, and forces a rebuild if they've drifted apart. A solution only stays untouched
when what it already references still matches reality.

### 3. Branch resolution, across an entire build pass, only ever walks toward `stable`

For any given target branch, resolving "which branch does this repository actually build on" only ever
walks from the target branch upward toward `stable` — never sideways to an unrelated branch, never
downward to something hotter. This means every branch that can possibly appear within one build pass
lies on a single line from the target branch up to `stable`. Two such branches are therefore always
comparable — one is always an ancestor of the other, or they're equal. A build pass can never produce
two branches that are unrelated siblings.

### Putting it together

- If `B` is rebuilt for the target branch and `A` depends on it: by (2), `A` is rebuilt too, on the
  same branch. Invariant holds trivially (equal branches).
- If `B` is *not* rebuilt and `A` depends on it: by (2bis), `A`'s reference to `B` must already be
  consistent with `B`'s last published state, and — by the same reasoning applied recursively to
  whatever build pass last touched `A` — that reference was itself established this exact way. `B`'s
  branch is therefore always on the ancestor chain of whatever branch `A` is ever built for, by (3) and
  by induction over every build pass that has ever run.

No two related packages can ever end up with incomparable branches, and a consumer can never be left
behind on a cooler branch than a dependency it still references — the moment that would happen, the
consumer is forced onto the hotter branch instead.

## The caveat

This is a guarantee about what CKli's own build pipeline computes and writes. It is *self-healing*
rather than *preventive*: if a package reference is manually edited to point at an arbitrary version,
bypassing CKli entirely, the invariant can be broken for a moment. The same reconciliation mechanism
described above ((2bis) and the discrepancy detection it relies on) is exactly what notices the drift
and forces a corrective rebuild the next time that solution is touched — it heals the violation rather
than having prevented it from ever existing.

## Where this is checked

A debug-only assertion in the Roadmap's per-solution build computation re-derives this exact condition
for every solution and every one of its direct requirements each time a build plan is computed: the
branch of the version about to be produced must equal, or be a descendant of, the branch of every
dependency's version. It only runs in Debug builds and exists as a safety net against a regression in
the mechanisms above — it does not implement the guarantee itself.

At publication time, `CKli.Publish.Plugin`'s **publication gate** checks the version-level
strengthening of this invariant: not merely that a package depends on a comparable, cooler-or-equal
branch, but that it depends on exactly the version the branch's profile offers. That check runs
before anything is pushed, which makes it preventive where the mechanisms above are self-healing.
It is per branch, and only the branch being published is gated: a publication on a cooler branch
necessarily invalidates the profiles of the hotter ones — they still reference the version it
supersedes — and those heal through their own next build, by (2bis). See
[`CKli.Publish.Plugin/README.md`](CKli.Publish.Plugin/README.md).

## How this relates to the Fix Workflow

This workflow owns "the current hot zone" — anything at or ahead of the last published stable version,
built and propagated live across the World. It deliberately does not reach backward into
already-superseded release lines: patching one of those is the Fix Workflow's job (`Fix-Workflow.md`),
which trades this workflow's transitive, branch-resolved propagation for a narrower, pinned-dependency
guarantee suited to isolated, one-off patches. A version is handled by exactly one of the two.
