namespace CK.Env
{
    /// <summary>
    /// Plugin for a <see cref="Env.GitFolder"/>.
    /// There must be one and only one public constructor that must not have a 'NormalizedPath branchPath' parameter
    /// (except of course if <see cref="IGitBranchPlugin"/> is also supported).
    /// </summary>
    public interface IGitPlugin
    {
        /// <summary>
        /// Gets the <see cref="Env.GitFolder"/> into which this plugin is registered.
        /// </summary>
        GitFolder GitFolder { get; }
    }
}
