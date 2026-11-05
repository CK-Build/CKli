using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// Raised by the "ckli repo add" or "ckli repo create" commands before anything is done: no remote repository has
/// been created yet and nothing is cloned.
/// </summary>
public sealed class RepoAddingEventArgs : WorldEventArgs
{
    readonly Uri _originUrl;
    readonly bool _isCreating;
    string _defaultBranchName;

    internal RepoAddingEventArgs( IActivityMonitor monitor,
                                  CKliEnv context,
                                  World world,
                                  Uri originUrl,
                                  bool isCreating )
        : base( monitor, context, world )
    {
        _originUrl = originUrl;
        _isCreating = isCreating;
        _defaultBranchName = "main";
    }

    /// <summary>
    /// Gets the url of the repository.
    /// </summary>
    public Uri OriginUrl => _originUrl;

    /// <summary>
    /// Gets whether the remote repository is created ("ckli repo create") or already exists ("ckli repo add").
    /// </summary>
    public bool IsCreating => _isCreating;

    /// <summary>
    /// Gets or sets the default branch of the repository. Defaults to "main".
    /// <list type="bullet">
    ///     <item>The remote repository is created with this default branch ("ckli repo create").</item>
    ///     <item>
    ///     When the cloned repository is empty, its initial commit is created on this branch (rather than on the
    ///     "init.defaultBranch" of the git configuration).
    ///     </item>
    ///     <item>
    ///     A created remote repository is empty: once the repository is added, this branch is pushed and becomes the
    ///     remote's default branch.
    ///     </item>
    /// </list>
    /// </summary>
    public string DefaultBranchName
    {
        get => _defaultBranchName;
        set
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( value );
            _defaultBranchName = value;
        }
    }
}
