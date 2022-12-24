namespace CK.Env.NodeSln
{
    /// <summary>
    /// The kind of project we can handle.
    /// </summary>
    public enum NodeProjectKind
    {
        /// <summary>
        /// A Node project is defined by a package.json file that doesn't
        /// contain a "workspaces": ["*"] property and has no angular.json file
        /// in the same directory.
        /// </summary>
        NodeProject,

        /// <summary>
        /// A Yarn workspace is actually a "worktree": its package.json contains a "workspaces": ["*"]
        /// where the "*" is a glob pattern that defines which subordinated NPMProjects that must be considered
        /// as child projects.
        /// <para>
        /// For us, when a package.json contains a "workspaces": ["*"], it is like a ".sln" that contains
        /// the projects that are all the folders with a package.json.
        /// </para>
        /// </summary>
        YarnWorkspace,

        /// <summary>
        /// An Angular workspace is defined by a project.json and an angular.json file that lists
        /// the subordinated projects.
        /// <para>
        /// Any package.json that don't appear in the angular.json file is ignored.
        /// </para>
        /// </summary>
        AngularWorkspace
    }


}


