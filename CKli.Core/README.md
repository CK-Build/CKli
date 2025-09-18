# Architecture

CKli.Core handles basic Git related functionalities: it manages Stack, World and Repos regardless
or their content.

This has been designed to be state-less: lazy loading and caching is almost everywhere but no cache
resynchronization is implemented, the Stack, World and Repos are intended to be used to complete one
intent (typically a command) and be discarded. The state is on the file system, not in memory.

Git repository handling (the notion of Solution, Projects, dependencies, branch management, etc.) are
implemented in "pseudo-plugins": they contribute to the core by providing `RepoMetaInfoProvider<T>`
concrete implementations that associate meta information to a `Repo`.
TODO: What are "WorldService"?

These are currently "pseudo-plugins" because the `CKli` application is statically linked to them but the
mid to long term goal is that these become real plugins through dynamic loading (or even through
precompilation of source plugins that would exist in the Stack repository...).




