# Ckli

# Command lines

### Clone
```
> ckli clone -?
Description:
  Clones a Stack and all its repositories to the local file system.
Usage:
  CKli clone <repository> [<directory>] [options]

Arguments:
  <repository>  The stack repository to clone from. Its name ends with '-Stack'.
  <directory>   Parent folder of the created stack folder. Defaults to the current directory. [default: C:\Dev]

Options:
  --private                                            Indicates a private repository.
  --allowDuplicate                                     Allows a repository that already exists in "stack list" to be
                                                       cloned.

> ckli clone https://github.com/CK-Build/CK-Build-Sample-Stack C:/Dev
```
Produces:
```
C:/Dev/CK-Build-Sample/
  .PublicStack/
  SampleA/
  SampleB/
  Misc/
    SampleC/
    SampleD/
```

### Area: Stack
`ckli stack` exposes stack related commands.

### stack list
`ckli stack list` lists the stack that have been cloned so far. This memory is kept in
a test file in user CKli folder (`%LocalAppData%\CKli` on Windows, `$HOME/.local/share/CKli` on Unix)
in a stupid text file (currently `StackRootRegistry.v0.txt`).

The path, remote url and whether the repository is public or not is listed. Duplicates
can exist (same stack name cloned at different paths) and are also listed: the oldest one is considered
to be the primary ones.

### World area
To avoid mistakes commands in this area will execute only on the World based on the current directory.

### world status
Dumps a git-like status for each repository of the World and the World global status.








