using CK.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Special content implementation that captures a <see cref="System.Exception"/>, intended
/// to be used as a <see cref="HttpResponseMessage.Content"/>.
/// <para>
/// This is managed by the <see cref="HttpGitHostingProvider"/> (because of the internal hook and how
/// it has to be instantiated that is better hidden). If this pattern deemed to be successful, we may
/// attempt to generalize it.
/// </para>
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
    readonly HttpRequestCancellationSource _cancellationSource;

    HttpExceptionContent( Exception ex, HttpRequestCancellationSource cancellationSource )
    {
        _ex = ex;
        _cancellationSource = cancellationSource;
    }

    /// <summary>
    /// Gets the exception.
    /// </summary>
    public Exception Exception => _ex;

    /// <summary>
    /// Gets whether the request has been canceled, and the cancellation source if it has been determined.
    /// </summary>
    public HttpRequestCancellationSource CancellationSource => _cancellationSource;

    internal static HttpResponseMessage CreateResponseMessage( HttpRequestMessage request,
                                                               Exception ex,
                                                               HttpRequestCancellationSource cancellationSource )
    {
        HttpResponseMessage response = new HttpResponseMessage( HttpExceptionStatusCode );
        response.ReasonPhrase = ex.Message;
        response.RequestMessage = request;
        response.Content = new HttpExceptionContent( ex, cancellationSource );
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

