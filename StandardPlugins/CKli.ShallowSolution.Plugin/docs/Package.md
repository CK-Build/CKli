`CKli.ShallowSolution.Plugin` is a plugin of the dotnet tool `CKli` that implements a read only file system model that
applies to Commits and/or actual file system (see `INormalizedFileProvider`, a modified .Net `IFileProvider` contract).

This enables a very simple .Net solution to be read from a `.slnx` file that can be on the file system or in the content of a commit
(no need to check out a commit to analyze its conventional content that is a `.slnx` file).

This package also implements a `MutableSolution` that can only be obtained from the file system. It supports package updates
(with or without NuGet central package management).










