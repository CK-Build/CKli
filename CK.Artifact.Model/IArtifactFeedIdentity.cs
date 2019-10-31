namespace CK.Core
{
    /// <summary>
    /// Models the identity of a packages' feed (packages are installable artifacts).
    /// </summary>
    public interface IArtifactFeedIdentity
    {
        /// <summary>
        /// Name of this feed. Must be unique for the <see cref="ArtifactType"/>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Identifies this feed. This is "<see cref="ArtifactType.Name"/>:<see cref="Name"/>" and must
        /// uniquely identify this feed.
        /// </summary>
        string TypedName { get; }

        /// <summary>
        /// Gets the artifact type that this feed supports.
        /// </summary>
        ArtifactType ArtifactType { get; }
    }
}
