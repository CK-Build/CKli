# CKli Full Builds
## “AllBuild” command overview 
The 3 main steps of this command are:  
1. Preparation   
  * We ensure that ZeroBuild projects are up-to-date.  
  * All the solutions are loaded and topologically sorted.
2. Prebuild: For each solution, in increasing rank order: 
  * Project dependencies are upgraded (with the packages built and produced by the previous solutions).   
  * A commit point is created with a message that describes the upgraded dependency versions.   
  * Based on the maximal number of commit points between the current head and the last release, the CI build     version number is computed and associated to the packages produced by the current solution. Note that a commit is not necessarily created here if the dependencies have not changed (since working folder is up-to-date).
3. Build: For each solution, in increasing rank order:
  * We upgrade any dependencies of the Build projects (ie. the CodeCakeBuilder project).
  * We amend the current commit if it is possible.
    * If not (we are on a fresh check-out or the branch has been pushed and no upgrade has been done during the pre-build phase), this triggers a retry of the build process. This could be called an edge case and we won’t detail this more here.
  * Solution’s CodeCakeBuilder is run. 
    * At its level, CodeCakeBuilder checks the packages with the version it has to build are not already present in its NuGet feeds. In such case build is skipped. 
 
Before building a Solution, CKli displays the ordered list of Solutions with a star * in front of the one being processed: 
```log
  -- Rank 0
    0 - CK-AspNet-Tester.sln
    1 - CK-Auth-Abstractions.sln
    2 - CK-MicroBenchmark.sln
    3 - CK-Reflection.sln
    4 - CK-UnitsOfMeasure.sln
    5 - CK-WeakAssemblyNameResolver.sln
    6 - json-graph-serializer.sln
    7 - Yodii-Script.sln
 -- Rank 1
    8 - CK-Text.sln
 -- Rank 2
    9 - CK-Core.sln
 -- Rank 3
    10 - CK-ActivityMonitor.sln
 -- Rank 4
    11 - CK-AmbientValues.sln
    12 - CK-CodeGen.sln
 *  13 - CK-Monitoring.sln
    14 - CK-SqlServer-Parser-Model.sln
 -- Rank 5
    15 - CK-AspNet.sln
    16 - CK-Testing.sln
 -- Rank 6
    17 - CK-Crs.sln
    18 - CK-Globbing.sln
    19 - CK-Setup.sln
    20 - CK-Setup-Dependency.sln
    21 - CK-SqlServer.sln
    22 - CK-SqlServer-Parser.sln
 -- Rank 7
    23 - CK-Database.sln
    24 - CKSetupRemoteStore.sln
    25 - CK-SqlServer-Dapper.sln
 -- Rank 8
    26 - CK-DB.sln
    27 - CK-DB-SqlCKTrait.sln
    28 - CK-Sqlite.sln
 -- Rank 9
    29 - CK-DB-Actor-ActorEMail.sln
    30 - CK-DB-GitHub.sln
    31 - CK-DB-TokenStore.sln
    32 - CK-DB-User-SimpleInvitation.sln
    33 - CK-DB-User-UserPassword.sln
 -- Rank 10
    34 - CK-AspNet-Auth.sln
 -- Rank 11
    35 - CK-DB-GuestActor.sln
 -- Rank 12
    36 - CK-DB-GuestActor-Acl.sln
```
