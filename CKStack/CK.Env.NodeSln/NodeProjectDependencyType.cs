namespace CK.Env.NodeSln
{
    /// <summary>
    /// Categorizes the type of a Node project dependency.
    /// </summary>
    public enum NodeProjectDependencyType
    {
        /// <summary>
        /// Non applicable or invalid.
        /// </summary>
        None,

        /// <summary>
        /// The version bound.
        /// </summary>
        VersionBound,

        /// <summary>
        /// 'file:' url that references a local folder relative to the package.
        /// </summary>
        LocalPath,

        /// <summary>
        ///  'file:' url that reference a tarball (.tgz) in a local feed.
        /// </summary>
        LocalFeedTarball,

        /// <summary>
        /// 'http://' or 'https://' url to a tarball (.tar.gz).
        /// </summary>
        UrlTar,

        /// <summary>
        /// 'git://', 'git+ssh://', 'git+http://', 'git+https://', or 'git+file://'
        /// url to a Git repository object.
        /// </summary>
        UrlGit,

        /// <summary>
        /// Simplified syntax for Git on GitHub: userOrOrganization/repo[(#|/)...].
        /// </summary>
        GitHub,

        /// <summary>
        /// A simple identifier that identifies a specific published tagged version.
        /// </summary>
        Tag,

        /// <summary>
        /// Yarn specific identifier, kind of project reference by project package name. Project must be in a Yarn Workspace.
        /// </summary>
        Workspace,

        /// <summary>
        /// Yarn specific identifier, kind of project reference by path.
        /// </summary>
        Portal,

        /// <summary>
        /// Any other version specification.
        /// </summary>
        OtherVersionSpec
    }

}


