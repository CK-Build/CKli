using System.Net.Http;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Models the possible reasons of a request cancellation.
/// </summary>
public enum HttpRequestCancellationSource
{
    /// <summary>
    /// The operation has not been canceled.
    /// </summary>
    None,

    /// <summary>
    /// The operation has been canceled by the user provided cancellation token.
    /// </summary>
    User,

    /// <summary>
    /// The operation has timed out (<see cref="HttpClient.Timeout"/>).
    /// </summary>
    Timeout,

    /// <summary>
    /// The operation has been canceled by a call to <see cref="HttpClient.CancelPendingRequests()"/>.
    /// </summary>
    CancelRequests,

    /// <summary>
    /// The operation has been canceled but we cannot determine the cancellation source.
    /// </summary>
    Other
}
