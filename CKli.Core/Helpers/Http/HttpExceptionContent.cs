using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Special content implementation that captures a <see cref="System.Exception"/>, intended
/// to be used as a <see cref="HttpResponseMessage.Content"/>.
/// <para>
/// This intentionally breaks the <see cref="HttpContent"/> contract: this is not serializable because
/// this lives on the receive side only.
/// </para>
/// </summary>
public sealed class HttpExceptionContent : HttpContent
{
    /// <summary>
    /// The exception code value.
    /// </summary>
    public const int ExceptionStatusCode = 666;

    /// <summary>
    /// The <see cref="ExceptionStatusCode"/> expressed as a <see cref="HttpStatusCode"/>.
    /// </summary>
    public const HttpStatusCode HttpExceptionStatusCode = (HttpStatusCode)666;

    readonly Exception _ex;

    /// <summary>
    /// Initializes a new content with the exception.
    /// </summary>
    /// <param name="ex">The exception.</param>
    public HttpExceptionContent( Exception ex )
    {
        _ex = ex;
    }

    /// <summary>
    /// Gets the exception.
    /// </summary>
    public Exception Exception => _ex;

    /// <summary>
    /// Creates a <see cref="HttpResponseMessage"/> with a <see cref="HttpExceptionContent"/> content.
    /// </summary>
    /// <param name="request">The originating content.</param>
    /// <param name="ex">The exception.</param>
    /// <returns>A response message with a exception content.</returns>
    public static HttpResponseMessage CreateResponseMessage( HttpRequestMessage request, Exception ex )
    {
        HttpResponseMessage response = new HttpResponseMessage( HttpExceptionStatusCode );
        response.ReasonPhrase = ex.Message;
        response.RequestMessage = request;
        response.Content = new HttpExceptionContent( ex );
        return response;
    }

    protected override void SerializeToStream( Stream stream, TransportContext? context, CancellationToken cancellationToken )
    {
        throw new NotSupportedException();
    }

    protected override Task SerializeToStreamAsync( Stream stream, TransportContext? context )
    {
        throw new NotSupportedException();
    }

    protected override bool TryComputeLength( out long length )
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Overridden to return the <see cref="Exception.ToString()"/>.
    /// </summary>
    /// <returns>The exception.</returns>
    public override string ToString() => _ex.ToString();
}

