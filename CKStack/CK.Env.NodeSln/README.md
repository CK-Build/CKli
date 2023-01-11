# CKli.NodeSln

This implements a simple model that manages a repository wide equivalent of a .Net solution file. A NodeSolution
is composed of one or more basic NodeProject, YarnWorkspace and/or AngularWorkspace.

This simple models must support Ckli and CodeCakeBuilder needs:

- For CKli, we need to determine the input/output artifacts:
  - the artifacts that are used by the repository;
  - the artifacts that **will** be produced by the repository;

This enables the dependency analysis and their management across the stack.

- For CodeCakeBuilder we need to know:
  - The top project paths in order to call the "yarn/npm build": this makes all Solution's artifacts ready to be created
    by "npm pack".
  - The path of all the directories where "npm pack" must be called to generate the NPM package (a `.tgz` file).

This enables the artifacts to be generated and collected into the `CodeCakeBuiler/Releases` folder.

The very simple `<NodeSolution>` element of the RepositoryInfo.xml file (and some discovery code in CodeCakeBuilder
and CKli provide us the required information:
```xml
<NodeSolution>
  <NodeProject Path="Clients/ck-observable-domain" />
  <AngularWorkspace Path="Clients/misc-components" />
  <YarnWorkspace Path="Clients/complex-demo" />
</NodeSolution>
```
For every type of top project, the `Path` folder contains the `package.json` manifest with:
- A `"private":true` property for workspaces.
- If `private` is false or missing, the `"name":"..."` must specify the final package name (including 
  its @scope: "@signature/webfrontauth").
- A required `"scripts": [ "build": "..." ]` entry that must build the project.
- An optional `"scripts": [ "test": "..." ]` entry that must run the tests.

CodeCakeBuilder will run "build" and "test" scripts when needed on these top projects.

## NodeProject (simple)
There is nothing more to it than the standard `package.json` manifest.

Both CKli and CodeCakeBuilder analyze the [dependencies](https://docs.npmjs.com/cli/v9/configuring-npm/package-json#dependencies):
- CKli uses them to dump and update the versions and to track the dependencies across a Stack (under construction).
- CodeCakeBuilder uses them to apply the SimpleGitVersion of the repository to the dependencies in the same repository 
  and to play with "file:...tgz" when called by CKli in local build mode (under construction). 

If the "npm pack" must be run in another directory because the build script can produce a clean `package.json` (with at
least no "scripts" nor "devDependencies" for instance) then a `"outputPath":"..."` can be added to the `package.json`.  

By default, "npm pack" is run (by CodeCakeBuilder) from the `Path` unless the `"outputPath"` is defined.

## AngularWorspace
The projects `package.json` must have its `"private":true` and a [`angular.json`](https://angular.io/guide/workspace-config)
file must exists. CKli and CodeCakeBuilder analyze this file to discover the actual artifacts that this workspace produces.
The "projects" property can contain Applications or Libraries.

We currently only support Libraries (Applications are planned to be supported through "CKArt" generic artifacts).

Angular libraries uses a `ng-package.json` located in project's path (next to the `package.json`) whose
"dest" property contains the output path. CodeCakeBuilder uses this to discover the `OutputPath` and run "npm pack".

_Note about dependencies:_
- Angular relies on the [path mapping feature](https://www.typescriptlang.org/docs/handbook/module-resolution.html#path-mapping)
  of TypeScript. The `tsconfig.json` is maintained by the [`ng`](https://angular.io/cli) command line interface tool.
  *Always use the `ng` tool** to play add/remove Applications or Libraries in an AngularWorkspace.
- When a project references another project of the workspace, the 0.0.0-0 version should be used: it is the TypeScript path 
  mapping that enables the compilation and CodeCakeBuilder that will update the version dependency to the repository's SimpleGitVersion.


## YarnWorkspace
A YarnWorkspace is a composition of projects (some of them can define other subordinate workspaces). 
Projects define Workspaces thanks to the `workspaces:[]` property of their `package.json`: when
subordinate projects are defined,`"private": true` MUST be specified since it is more a "folder" than a project.

Both CKli and CodeCakeBuilder analyze the top project and flattens the workspaces to obtain the list of all the projects.

For CodeCakeBuilder, these projects are like NodeProjects described above: the `"outputPath"` can be used to tell CodeCakeBuilder
where "npm pack" should run.
