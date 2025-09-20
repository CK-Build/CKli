using CK.Core;

namespace CKli.Core;

/// <summary>
/// The <see cref="RepoInfo.ErrorState"/>.
/// </summary>
public enum RepoInfoErrorState
{
    /// <summary>
    /// No error: the information is valid.
    /// </summary>
    None,

    /// <summary>
    /// The information cannot be computed and must not be used. The <see cref="RepoInfo.ErrorReason"/>
    /// should be a string that explains the reason but can be another <see cref="RepoInfo"/>
    /// (that is invalid or on error) or a <see cref="CKExceptionData"/>.
    /// <para>
    /// This doesn't prevent other information to be computed: some providers may ignore an invalid dependency.
    /// </para>
    /// </summary>
    Invalid,

    /// <summary>
    /// Computing the information failed. The <see cref="RepoInfo.ErrorReason"/> can be another <see cref="RepoInfo"/>
    /// (that is invalid or on error), an error message (a string) or a <see cref="CKExceptionData"/>.
    /// <para>
    /// This prevents other information to be computed from this one: they should also be created on error.
    /// </para>
    /// </summary>
    Error
}
