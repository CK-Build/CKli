namespace CK.Env.Plugin
{
    /// <summary>
    /// Characterizes a build.
    /// </summary>
    public enum BuildType
    {
        /// <summary>
        /// Whether the build targets the Local environment.
        /// </summary>
        IsTargetLocal = 1,

        /// <summary>
        /// Whether the build targets the CI environment.
        /// </summary>
        IsTargetCI = 2,

        /// <summary>
        /// Whether the build targets the Release environment.
        /// </summary>
        IsTargetRelease = 4,

        /// <summary>
        /// Whether the build  requires to temporarily alter the working folder:
        /// a <see cref="GitRepository.ResetHard"/> will be done after the build.
        /// </summary>
        IsUsingDirtyFolder = 8,

        /// <summary>
        /// Whether the ZeroVersion must be used. This requires an access to the <see cref="IEnvLocalFeedProvider.ZeroBuild"/>.
        /// When not set, the CodeCakeBuilder project is "dotnet run" (ie, compiled and run).
        /// </summary>
        WithZeroBuilder = IsUsingDirtyFolder | 16,

        /// <summary>
        /// Whether the builder must push the artifacts to the remotes.
        /// </summary>
        WithPushToRemote = 32,

        /// <summary>
        /// Whether the builder must execute unit tests.
        /// </summary>
        WithUnitTests = 64,

        /// <summary>
        /// Local build on 'local' branch.
        /// </summary>
        Local = IsTargetLocal,

        /// <summary>
        /// Local only CI build on 'develop' branch. Artefacts are kept locally.
        /// </summary>
        CI = IsTargetCI | IsUsingDirtyFolder,

        /// <summary>
        /// Release build. Artefacts are kept locally and always use the ZeroBuilder.
        /// </summary>
        Release = IsTargetRelease | IsUsingDirtyFolder | WithZeroBuilder
    }
}
