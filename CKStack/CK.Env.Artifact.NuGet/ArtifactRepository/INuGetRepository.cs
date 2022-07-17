namespace CK.Env.NuGet
{
    /// <summary>
    /// A NuGet repository has a name. The <see cref="IArtifactRepository.UniqueRepositoryName"/>
    /// is derived from it.
    /// </summary>
    public interface INuGetRepository : IArtifactRepository
    {
        /// <summary>
        /// Gets the name of this repository.
        /// </summary>
        string Name { get; }
    }
}
