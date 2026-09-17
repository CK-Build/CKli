# CKli.Build.Plugin.Testing

Optional test harness that speeds up CKli tests by short-circuiting real build/test/package steps
with fakes.

This doesn't replace the `Remotes` based tests (that `dotnet build/tests/package` and `dotnet package list`): these "heavy"
tests are true integration tests.

## The fake build is owned by the environment

`BuildPlugin`'s builder function is a static hook, so only one fake can be installed at a time.
`FakeBuildTestEnv` installs its own `FakeBuildAsync` — an **instance** method (`FakeBuildTestEnv.FakeBuild.cs`) —
in its constructor and restores the previous one on `Dispose`. Tests create one environment at a time, so
that is safe.

The consequence that matters: everything the fake build reads about a repository belongs to *that*
environment. A test declares it on the `FakeBuildRepo` returned by `FakeBuildWorld.CreateRepoAsync`, and the
environment joins the CKli `Repo` it is handed back to that `FakeBuildRepo` through a per-environment
registry. That registry is keyed by **origin url**, not by repository name: a name (`RepositoryName` is the
path's last part) is not unique in general, and the url is what a *second clone* of the same remotes shares
with the first — a coworking test builds from both.

What can be declared:

| On `FakeBuildRepo` | Effect |
|---|---|
| `TransitivePackages` | The transitive packages recorded in `BuildContentInfo`. The fake restores nothing, so declaring them is the only way a transitive set can exist here; `default` (the default) means *unknown*, which is not the empty array. Setting it also rewrites the initial version tag's annotation, in place on its own commit. |
| `FailBuild` | Makes this repository's fake build fail. It bails out before anything is written — in particular before `ApplyReleaseBuildTag` — like a build that does not compile. Settable back to `false`, so a test can fail a build, observe what it left behind, then build again. |

`FailBuild` is how a **partially failed roadmap** is arranged: failing one solution stops
`RoadmapExecutor` from promoting the `building/` tags of the ones that did succeed, which is the state
`Plugins.Tests/PartialBuildFailureTests` pins.

A fake build runs on the roadmap's own per-solution monitor (builds go up to `--max-dop`), so
`TestHelper.Monitor.CollectTexts(...)` — an `IActivityMonitorClient` on one monitor — never sees what it logs.
Use `GrandOutput.Default.CreateMemoryCollector(...)` and its `ExtractCurrentTexts()`, which collects from the
dispatcher and waits for the queued entries first. See `CKli.Testing`"s README for the details.
