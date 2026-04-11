using CK.Core;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Raised by the "ckli repo add" or "ckli repo create" commands.
/// </summary>
public sealed class RepoAddedEvent : WorldEvent
{
    readonly GitRepository _gitRepository;
    readonly XElement _repoDefinition;

    internal RepoAddedEvent( IActivityMonitor monitor,
                             World world,
                             GitRepository gitRepository,
                             XElement repoDefinition )
        : base( monitor, world )
    {
        _gitRepository = gitRepository;
        _repoDefinition = repoDefinition;
    }

    /// <summary>
    /// Gets the Xml element that will be added to the <see cref="WorldDefinitionFile"/>.
    /// <para>
    /// It is already configured with its Url attribute (that should not be removed).
    /// </para>
    /// </summary>
    public XElement RepoDefinition => _repoDefinition;

    /// <summary>
    /// Gets the just cloned git repository of the new Repo.
    /// </summary>
    public GitRepository GitRepository => _gitRepository;

}
