namespace CK.Env.NodeSln
{
    /// <summary>
    /// The kind of project we can handle.
    /// </summary>
    public enum NodeProjectKind
    {
        /// <summary>
        /// A Node project is defined by a package.json file that doesn't
        /// contain a "workspaces": [] property and has no angular.json file
        /// in the same directory and is not in a workspace.
        /// A node project must not contain subordinated projects or workspaces.
        /// </summary>
        NodeProject,

        /// <summary>
        /// A Yarn workspace is actually a "worktree": its package.json contains a "workspaces": ["P1","Sub/P2",...]
        /// No glob pattern is supported.
        /// <para>
        /// For us, when a package.json contains a "workspaces": ["P1", "P2"], it is like a ".sln" that contains
        /// the project where each folder must contain a package.json that defines the <see cref="NodeSubProject"/>.
        /// This NOT recursive: a workspace must not contain subordinated workspaces.
        /// </para>
        /// </summary>
        YarnWorkspace,

        /// <summary>
        /// An Angular workspace is defined by a project.json and an angular.json file that lists
        /// the subordinated projects.
        /// <para>
        /// Any package.json that don't appear in the angular.json file is ignored.
        /// This NOT recursive: a workspace must not contain subordinated workspaces.
        /// </para>
        /// </summary>
        AngularWorkspace,

        /// <summary>
        /// A Node project contained in a <see cref="YarnWorkspace"/> or <see cref="AngularWorkspace"/>.
        /// It must not contain subordinated projects or workspaces.
        /// </summary>
        NodeSubProject
    }

}


