namespace CKli.Build.Plugin;

/// <summary>
/// What a build does with its impacts: the files it created, modified or deleted in the working folder
/// (files that the ".gitignore" files ignore are not impacts).
/// </summary>
public enum BuildImpacts
{
    /// <summary>
    /// The build fails when it has impacts: the error lists them.
    /// This is the default and the only mode of "ckli publish", "ckli *publish" and "ckli fix build".
    /// </summary>
    Error,

    /// <summary>
    /// The impacts are amended to the built commit ("Changes made by the build.") and the amended commit
    /// is built once more: the version is tagged on the commit that contains them and the build of the
    /// amended commit must have no impact. The built commit must be the tip of the checked out branch
    /// and must not have been pushed.
    /// </summary>
    Commit,

    /// <summary>
    /// The impacts are erased by the reset that follows the build.
    /// </summary>
    Discard
}
