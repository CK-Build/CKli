using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// Required base class of information associated to a <see cref="Repo"/>.
/// <para>
/// This is used to signal and propagate any error or incapacity to produce an
/// information accross dependent <see cref="RepoInfoProvider{T}"/>.
/// </para>
/// </summary>
public abstract class RepoInfo
{
    readonly object? _errorReason;
    readonly RepoInfoErrorState _errorState;
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
    /// Error constructor.
    /// </summary>
    /// <param name="errorState">Must not be <see cref="RepoInfoErrorState.None"/>.</param>
    /// <param name="reason">
    /// The reason.
    /// Must be a string, an exception, a <see cref="CKExceptionData"/> or another RepoMetaInfo
    /// that must be invalid or on error.
    /// </param>
    protected RepoInfo( Repo repo, RepoInfoErrorState errorState, object reason )
    {
        Throw.CheckNotNullArgument( repo );
        Throw.CheckArgument( errorState is not RepoInfoErrorState.None );
        Throw.CheckArgument( (reason is string s && !string.IsNullOrWhiteSpace(s))
                             || (reason is RepoInfo info && info.ErrorState is RepoInfoErrorState.Error or RepoInfoErrorState.Invalid)
                             || reason is CKExceptionData
                             || reason is Exception );
        _repo = repo;
        _errorState = errorState;
        _errorReason = reason is Exception ex ? CKExceptionData.CreateFrom( ex ) : reason;
    }

    /// <summary>
    /// Gets the repo.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the basic <see cref="RepoInfoErrorState"/>.
    /// </summary>
    public RepoInfoErrorState ErrorState => _errorState;

    /// <summary>
    /// Gets the error reason. It is a non empty string, a <see cref="CKExceptionData"/> or another RepoMetaInfo
    /// that is invalid or on error.
    /// </summary>
    public object? ErrorReason => _errorReason;

}
