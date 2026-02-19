using CK.Core;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
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
    CKExceptionData? _exData;
    byte[]? _utf8Text;

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

    CKExceptionData ToData() => _exData ??= CKExceptionData.CreateFrom( _ex );
    byte[] ToUtf8Text() => _utf8Text ??= Encoding.UTF8.GetBytes( ToData().ToString() );

    /// <summary>
    /// Serializes the current object to the specified stream using UTF-8 encoding.
    /// </summary>
    /// <param name="stream">The stream to which the object will be serialized. The stream must be writable.</param>
    /// <param name="context">An optional transport context that provides additional information about the transport operation. This parameter
    /// may be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the serialization operation.</param>
    protected override void SerializeToStream( Stream stream, TransportContext? context, CancellationToken cancellationToken )
    {
        stream.Write( ToUtf8Text() );
    }

    /// <summary>
    /// Asynchronously writes the current object's UTF-8 encoded representation to the specified stream.
    /// </summary>
    /// <remarks>The object is serialized as a UTF-8 encoded byte array before being written to the
    /// stream.</remarks>
    /// <param name="stream">The stream to which the object will be serialized. The stream must be writable.</param>
    /// <param name="context">An optional transport context that provides additional information about the transport layer. This parameter can
    /// be null.</param>
    /// <returns>A task that represents the asynchronous write operation to the stream.</returns>
    protected override Task SerializeToStreamAsync( Stream stream, TransportContext? context )
    {
        var bytes = ToUtf8Text();
        return stream.WriteAsync( bytes, 0, bytes.Length );
    }

    /// <summary>
    /// Attempts to calculate the length, in bytes, of the UTF-8 encoded text representation of the content.
    /// </summary>
    /// <param name="length">When this method returns, contains the length, in bytes, of the UTF-8 encoded text representation.</param>
    /// <returns>Always returns <see langword="true"/>, indicating that the length was successfully computed.</returns>
    protected override bool TryComputeLength( out long length )
    {
        length = ToUtf8Text().Length;
        return true;
    }

    /// <summary>
    /// Overridden to return the <see cref="Exception.ToString()"/>.
    /// </summary>
    /// <returns>The exception.</returns>
    public override string ToString() => _ex.ToString();
}

