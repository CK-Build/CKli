namespace CKli.Core.GitHosting;

/// <summary>
/// Result of a Git hosting operation.
/// </summary>
public class GitHostingOperationResult
{
    /// <summary>
    /// Indicates whether the operation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// HTTP status code from the API response, if applicable.
    /// </summary>
    public int? HttpStatusCode { get; }

    /// <summary>
    /// Indicates whether the error was due to authentication failure.
    /// </summary>
    public bool IsAuthenticationError => HttpStatusCode == 401 || HttpStatusCode == 403;

    /// <summary>
    /// Indicates whether the resource was not found.
    /// </summary>
    public bool IsNotFound => HttpStatusCode == 404;

    /// <summary>
    /// Indicates whether rate limiting was hit.
    /// </summary>
    public bool IsRateLimited => HttpStatusCode == 429;

    protected GitHostingOperationResult( bool success, string? errorMessage, int? httpStatusCode )
    {
        Success = success;
        ErrorMessage = errorMessage;
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static GitHostingOperationResult Ok() => new( true, null, null );

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static GitHostingOperationResult Fail( string errorMessage, int? httpStatusCode = null )
        => new( false, errorMessage, httpStatusCode );
}

/// <summary>
/// Result of a Git hosting operation that returns data.
/// </summary>
/// <typeparam name="T">The type of data returned on success.</typeparam>
public sealed class GitHostingOperationResult<T> : GitHostingOperationResult
{
    /// <summary>
    /// The data returned on success.
    /// </summary>
    public T? Data { get; }

    GitHostingOperationResult( bool success, T? data, string? errorMessage, int? httpStatusCode )
        : base( success, errorMessage, httpStatusCode )
    {
        Data = data;
    }

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
    public static GitHostingOperationResult<T> Ok( T data )
        => new( true, data, null, null );

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public new static GitHostingOperationResult<T> Fail( string errorMessage, int? httpStatusCode = null )
        => new( false, default, errorMessage, httpStatusCode );
}
