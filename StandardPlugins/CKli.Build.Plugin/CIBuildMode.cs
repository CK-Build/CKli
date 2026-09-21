namespace CKli.Build.Plugin;

enum CIBuildMode
{
    /// <summary>
    /// "--release": builds regular exploratory, prerelease or stable versions.
    /// This is the exception: the build commands are in CI by default.
    /// </summary>
    Release,

    /// <summary>
    /// The default: builds CI versions.
    /// </summary>
    CI,

    /// <summary>
    /// "--ci.0": extends <see cref="CI"/> to build a ci.0 version when a released version
    /// is already available on the commit.
    /// </summary>
    CIForce
};
