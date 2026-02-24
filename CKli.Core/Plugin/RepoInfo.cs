using CK.Core;

namespace CKli.Core;

/// <summary>
/// Required base class of information associated to a <see cref="Repo"/>.
/// </summary>
public abstract class RepoInfo
{
    readonly Repo _repo;

    /// <summary>
    /// Success constructor.
    /// </summary>
    protected RepoInfo( Repo repo )
    {
        Throw.CheckNotNullArgument( repo );
        _repo = repo;
    }

    /// <summary>
    /// Gets the repo.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets whether one or more issues must be resolved before anything serious can be done with this <see cref="Repo"/>.
    /// <para>
    /// This defaults to false.
    /// </para>
    /// </summary>
    public virtual bool HasIssue => false;
}
