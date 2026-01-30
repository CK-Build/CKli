using CK.Core;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Resolvers can be registered by calling <see cref="GitHostingProvider.RegisterPluginResolver"/> from
/// any <see cref="PluginBase.Initialize(IActivityMonitor)"/> and are automatically released when plugins
/// are unloaded.
/// <para>
/// If a resolver needs to cleanup resources, the plugin should be 
/// </para>
/// </summary>
public interface IPluginGitHostingResolver
{
    /// <summary>
    /// First possibility: this can resolve a provider based on the repository url.
    /// The provider is identified by the <see cref="GitRepositoryKey.AccessKey"/>: whether 
    /// it is a public or private repository and its <see cref="IGitRepositoryAccessKey.PrefixPAT"/>.
    /// This cannot be changed: the returned provider will handle all repositories that share the same
    /// PATs and public/private access.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="baseUrl">The <see cref="GitHostingProvider.BaseUrl"/> to use to instantiate the provider on success.</param>
    /// <param name="gitKey">The repository with its url and precomputed <see cref="GitRepositoryKey.AccessKey"/> (cannot be altered).</param>
    /// <param name="authority">The authority component of the repository url to consider.</param>
    /// <returns>A non null provider if it can be resolved, null otherwise.</returns>
    GitHostingProvider? TryCreateFromUrlPattern( IActivityMonitor monitor,
                                                 string baseUrl,
                                                 GitRepositoryKey gitKey,
                                                 string authority );
}
