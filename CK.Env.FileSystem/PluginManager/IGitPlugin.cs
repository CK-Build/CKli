namespace CK.Env
{
    /// <summary>
    /// Plugin for a <see cref="GitFolder"/>.
    /// There must be one and only one public constructor that must not have a 'NormalizedPath branchPath' parameter
    /// (except of course if <see cref="IGitBranchPluginCollection"/> is not also supported).
    /// </summary>
    public interface IGitPlugin
    {
        /// <summary>
        /// Gets the <see cref="GitFolder"/> into which this plugin is registered.
        /// </summary>
        GitFolder Folder { get; }
    }
}
