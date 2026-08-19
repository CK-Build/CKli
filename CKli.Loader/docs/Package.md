`CKli.Loader` is a crucial (but invisible) part of the dotnet tool `CKli` infrastructure that is
in charge of loading the plugins in a collectible `AssemblyLoadContext`.

This package requires `CKli.Plugins.Core` and orchestrates the plugin discovery (by reflection), the code
generation (of the plugins instantiation and command handlers) and the load of the `CKli.Plugins` assembly
that references the actual plugins (by package references or directly in source code in C# projects).





