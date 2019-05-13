namespace CK.Env
{
    /// <summary>
    /// Basic global changes in a project folder.
    /// </summary>
    public enum DirectoryDiffType
    {
        /// <summary>
        /// No change detected.
        /// </summary>
        None,

        /// <summary>
        /// Package is new.
        /// </summary>
        NewPackage,

        /// <summary>
        /// Someting changed.
        /// </summary>
        Changed
    }
}
