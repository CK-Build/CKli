using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.Tests.GitHosting.Mocks;

/// <summary>
/// A mock HTTP message handler for testing HTTP client operations.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    readonly Queue<HttpResponseMessage> _responses = new();
    readonly List<HttpRequestMessage> _requests = new();

    /// <summary>
    /// Gets all requests that were made.
    /// </summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    /// <summary>
    /// Enqueues a response to be returned for the next request.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="content">The response body content.</param>
    /// <param name="contentType">The content type (default: application/json).</param>
    public void EnqueueResponse( HttpStatusCode statusCode, string content, string contentType = "application/json" )
    {
        var response = new HttpResponseMessage( statusCode )
        {
            Content = new StringContent( content, Encoding.UTF8, contentType )
        };
        _responses.Enqueue( response );
    }

    /// <summary>
    /// Enqueues a successful JSON response.
    /// </summary>
    /// <param name="content">The JSON response body.</param>
    public void EnqueueJsonResponse( string content )
    {
        EnqueueResponse( HttpStatusCode.OK, content );
    }

    /// <summary>
    /// Enqueues a 201 Created response.
    /// </summary>
    /// <param name="content">The JSON response body.</param>
    public void EnqueueCreatedResponse( string content )
    {
        EnqueueResponse( HttpStatusCode.Created, content );
    }

    /// <summary>
    /// Enqueues a 404 Not Found response.
    /// </summary>
    /// <param name="message">The error message.</param>
    public void EnqueueNotFoundResponse( string message = "Not Found" )
    {
        EnqueueResponse( HttpStatusCode.NotFound, $"{{\"message\":\"{message}\"}}" );
    }

    /// <summary>
    /// Enqueues a 401 Unauthorized response.
    /// </summary>
    /// <param name="message">The error message.</param>
    public void EnqueueUnauthorizedResponse( string message = "Bad credentials" )
    {
        EnqueueResponse( HttpStatusCode.Unauthorized, $"{{\"message\":\"{message}\"}}" );
    }

    /// <summary>
    /// Enqueues a 403 Forbidden response.
    /// </summary>
    /// <param name="message">The error message.</param>
    public void EnqueueForbiddenResponse( string message = "Forbidden" )
    {
        EnqueueResponse( HttpStatusCode.Forbidden, $"{{\"message\":\"{message}\"}}" );
    }

    /// <summary>
    /// Enqueues a 429 Rate Limited response.
    /// </summary>
    public void EnqueueRateLimitResponse()
    {
        EnqueueResponse( HttpStatusCode.TooManyRequests, "{\"message\":\"API rate limit exceeded\"}" );
    }

    /// <summary>
    /// Enqueues a 204 No Content response (for successful DELETE).
    /// </summary>
    public void EnqueueNoContentResponse()
    {
        _responses.Enqueue( new HttpResponseMessage( HttpStatusCode.NoContent ) );
    }

    /// <summary>
    /// Enqueues a delayed response for testing cancellation and timeouts.
    /// </summary>
    /// <param name="delay">The delay before returning the response.</param>
    /// <param name="content">The JSON response body.</param>
    public void EnqueueDelayedResponse( TimeSpan delay, string content )
    {
        _delayedResponses.Enqueue( (delay, new HttpResponseMessage( HttpStatusCode.OK )
        {
            Content = new StringContent( content, Encoding.UTF8, "application/json" )
        }) );
    }

    readonly Queue<(TimeSpan Delay, HttpResponseMessage Response)> _delayedResponses = new();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
    {
        _requests.Add( request );

        // Check cancellation before processing
        cancellationToken.ThrowIfCancellationRequested();

        // Handle delayed responses first
        if( _delayedResponses.Count > 0 )
        {
            var (delay, response) = _delayedResponses.Dequeue();
            await Task.Delay( delay, cancellationToken );
            return response;
        }

        if( _responses.Count == 0 )
        {
            throw new InvalidOperationException( "No response enqueued for request." );
        }

        return _responses.Dequeue();
    }

    /// <summary>
    /// Clears all enqueued responses and recorded requests.
    /// </summary>
    public void Clear()
    {
        _responses.Clear();
        _requests.Clear();
    }

    /// <summary>
    /// Gets the request body as a string.
    /// </summary>
    /// <param name="requestIndex">The index of the request (default: 0).</param>
    /// <returns>The request body as a string.</returns>
    public async Task<string?> GetRequestBodyAsync( int requestIndex = 0 )
    {
        if( requestIndex >= _requests.Count ) return null;
        var request = _requests[requestIndex];
        if( request.Content == null ) return null;
        return await request.Content.ReadAsStringAsync();
    }
}
