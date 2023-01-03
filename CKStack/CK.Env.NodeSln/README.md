# CKli.NodeSln

This implements a simple model that manages a repository wide equivalent of a .Net solution file. A NodeSolution
is composed of one or more basic NodeProject, YarnWorkspace and/or AngularWorkspace.

- NodeProject is defined by its usual `package.json` manifest.
- A YarnWorkspace (https://yarnpkg.com/features/workspaces) can contain [`NodeSubProject`](ProjectTypes/NodeSubProject.cs)
  that must be explicitly listed in the `workspaces:[]` property of its manifest.
- An AngularWorkspace  (https://angular.io/guide/file-structure#multiple-projects) also contains `NodeSubProject`
  listed in the [`angular.json`](https://angular.io/guide/workspace-config) file.

This model is intentionally simple:
- Workspaces are not recursive.  
- Few properties are checked, the most important one is the `private` field of the `package.json` that states
  whether the project eventually generates a package or is purely local (and should not be published).

The `RepositoryInfo.xml` file must contain the `<NodeSolution>` element with the root projects that are
defined by the required `Path` property.

```xml
<NodeSolution>
  <NodeProject Path="Clients/ck-observable-domain" />
  <YarnWorkspace Path="Clients/complex-demo" />
  <AngularWorkspace Path="Clients/misc-components" OutputPath="Clients/misc-components/published" />
</NodeSolution>
```

The `OutputPath` property is optional: it is a path to the folder that contains the published package(s).
When not specified, this defaults to the project's `Path`.


