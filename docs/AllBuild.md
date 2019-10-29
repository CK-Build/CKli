# CKli Full Builds
## “AllBuild” command overview 
The 3 main steps of this command are:  
1. Preparation   
  * We ensure that ZeroBuild projects are up-to-date.  
  * All the solutions are loaded and topologically sorted.
2. Prebuild: For each solution, in increasing rank order: 
  * Project dependencies are upgraded (with the packages built and produced by the previous solutions).   
  * A commit point is created with a message that describes the upgraded dependency versions.   
  * Based on the maximal number of commit points between the current head and the last release, the CI build version number is computed and associated to the packages produced by the current solution. Note that a commit is not necessarily created here if the dependencies have not changed (since working folder is up-to-date).
3. Build: For each solution, in increasing rank order:
  * We upgrade any dependencies of the Build projects (ie. the CodeCakeBuilder project).
  * We amend the current commit if it is possible.
    * If not (we are on a fresh check-out or the branch has been pushed and no upgrade has been done during the pre-build phase), this triggers a retry of the build process. This could be called an edge case and we won’t detail this more here.
  * Solution’s CodeCakeBuilder is run. 
    * At its level, CodeCakeBuilder checks the packages with the version it has to build are not already present in its NuGet feeds. In such case build is skipped. 

Before building a Solution, CKli displays the ordered list of Solutions with a star * in front of the one being processed: 
```log
-- Rank 0
    0 - CodeCake.sln
*   1 - CSemVer-Net.sln
-- Rank 1
    2 - SGV-Net.sln
```

