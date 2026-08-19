`CKli.VersionTag.Plugin` is a plugin of the dotnet tool `CKli`. It is in charge of the tags on the repositories' commits
that look like a version number.

The `VersionTagInfo` provides the set of tags defined in the range defined by the per-repository plugin configuration
(Min/MaxVersion to consider for the World).

It depends on the `CKli.ReleaseDatabase.Plugin` because it updates the cached database.










