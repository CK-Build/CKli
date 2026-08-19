`CKli.Plugins.Core` is part of the dotnet tool `CKli` infrastructure: plugins must depend on this
package. 

Plugins can depend from each other (and only from each others): a simple Dependency Injection
is implemented. Plugins are automatically compiled and loaded in a collectible `AssemblyLoadContext`
by the `CKli.Loader` package.




