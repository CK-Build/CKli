# CKli.Build.Plugin.Testing

Optional test harness that speeds up CKli tests by short-circuiting real build/test/package steps
with fakes.

This doesn't replace the `Remotes` based tests (that `dotnet build/tests/package` and `dotnet package list`): these "heavy"
tests are true integration tests.

