using CK.Core;
using System.Collections.Immutable;

namespace CKli.Build.Plugin;

/// <summary>
/// What a <see cref="BuilderFunction"/> produced. The packages are not published yet and the version is not
/// tagged: this is done by the build plugin once the impacts of the build on the working folder have been handled
/// (see <see cref="BuildImpacts"/>).
/// </summary>
public sealed class BuildOutput
{
    /// <summary>
    /// Initializes a new build output.
    /// </summary>
    /// <param name="packagesFolder">The temporary folder that contains the produced ".nupkg" files.</param>
    /// <param name="consumed">The consumed packages (strictly sorted).</param>
    /// <param name="transitive">The transitive packages (default when unknown).</param>
    /// <param name="assetsFolder">The folder of the published deployment assets (default when there is none).</param>
    /// <param name="assetFileNames">The deployment asset file names.</param>
    /// <param name="assetsNotGenerated">True when the repository has a "Deployment/GenerateAssets.cs" that has not been run.</param>
    /// <param name="assetsRequest">The values given to "Deployment/GenerateAssets.cs" (default or empty for everything).</param>
    public BuildOutput( NormalizedPath packagesFolder,
                        ImmutableArray<PackageInstance> consumed,
                        ImmutableArray<PackageInstance> transitive,
                        NormalizedPath assetsFolder,
                        ImmutableArray<string> assetFileNames,
                        bool assetsNotGenerated = false,
                        ImmutableArray<string> assetsRequest = default )
    {
        Throw.CheckArgument( !packagesFolder.IsEmptyPath );
        PackagesFolder = packagesFolder;
        Consumed = consumed;
        Transitive = transitive;
        AssetsFolder = assetsFolder;
        AssetFileNames = assetFileNames;
        AssetsNotGenerated = assetsNotGenerated;
        AssetsRequest = assetsRequest.IsDefault ? [] : assetsRequest;
    }

    /// <summary>
    /// Gets whether the repository has a "Deployment/GenerateAssets.cs" that has not been run
    /// (see <see cref="ArtifactHandler.Plugin.BuildContentInfo.AssetsNotGenerated"/>).
    /// </summary>
    public bool AssetsNotGenerated { get; }

    /// <summary>
    /// Gets the values given to "Deployment/GenerateAssets.cs": empty for everything
    /// (see <see cref="ArtifactHandler.Plugin.BuildContentInfo.AssetsRequest"/>).
    /// </summary>
    public ImmutableArray<string> AssetsRequest { get; }

    /// <summary>
    /// Gets the temporary folder that contains the produced ".nupkg" files.
    /// The build plugin deletes it once the packages are published in the local feed.
    /// </summary>
    public NormalizedPath PackagesFolder { get; }

    /// <summary>
    /// Gets the consumed packages.
    /// </summary>
    public ImmutableArray<PackageInstance> Consumed { get; }

    /// <summary>
    /// Gets the transitive packages (default when they are unknown).
    /// </summary>
    public ImmutableArray<PackageInstance> Transitive { get; }

    /// <summary>
    /// Gets the folder of the published deployment assets (the empty path when there is none).
    /// </summary>
    public NormalizedPath AssetsFolder { get; }

    /// <summary>
    /// Gets the deployment asset file names.
    /// </summary>
    public ImmutableArray<string> AssetFileNames { get; }
}
