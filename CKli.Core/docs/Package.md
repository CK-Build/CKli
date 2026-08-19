`CKli.Core` is the core implementation of the dotnet tool `CKli` (plugins depends on it through their
reference to `CKli.Plugins.Core`).

It handles basic Git related functionalities: it manages Stack, World and Repos regardless or their content.

How Git repository content is handled (the notion of Solution, Projects, dependencies, branch management, etc.) are
implemented by **Plugins**. Plugins are services that can be "sourced based" (developed locally
in the Stack repository and compiled on-demand) or packaged as regular NuGet packages.







