# Architecture

CKli.Core handles basic Git related functionalities: it manages Stack, World and Repos regardless
or their content.

This has been designed to be state-less: lazy loading and caching is almost everywhere but minimal cache
resynchronization is implemented, the Stack, World and Repos are intended to be used to complete one
intent (typically a command) and be discarded. The state is on the file system, not in memory.

Git repository handling (the notion of Solution, Projects, dependencies, branch management, etc.) are
implemented by `WorldPlugin`. Plugins are services that can be "sourced based" (developped locally
in the Stack repository and compiled on-demand) or packaged as regular NuGet packages.





