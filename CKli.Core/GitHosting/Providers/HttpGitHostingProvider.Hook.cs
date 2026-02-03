using CK.Core;
using CKli.Core.GitHosting.Models.GitHub;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting.Providers;

public abstract partial class HttpGitHostingProvider
{
    sealed class Hook : DelegatingHandler
    {
        readonly HttpGitHostingProvider _provider;
        readonly IActivityMonitor _monitor;
        readonly RetryHelper _retryHelper;
        readonly CancellationToken _userCancellation;
        // Required to handle the Timeout detection.
        internal HttpClient? _httpClient;
        long _startedTickCount;

        internal Hook( HttpGitHostingProvider provider,
                       IActivityMonitor monitor,
                       RetryHelper retryHelper,
                       CancellationToken userCancellation )
            : base( provider._handler )
        {
            _provider = provider;
            _monitor = monitor;
            _retryHelper = retryHelper;
            _userCancellation = userCancellation;
        }

        protected override HttpResponseMessage Send( HttpRequestMessage request, CancellationToken cancellationToken )
        {
            throw new NotSupportedException( "GitHostingProvider API is only async." );
        }

        protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken finalCancellation )
        {
            _startedTickCount = Environment.TickCount64;
            return _provider.OnSendHookAsync( _monitor, request, BaseSendAsync, _retryHelper, finalCancellation );
        }

        async Task<HttpResponseMessage> BaseSendAsync( HttpRequestMessage request, CancellationToken finalCancellation )
        {
            try
            {
                return await base.SendAsync( request, finalCancellation ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                // If the linked cancellation token source was canceled, but cancellation wasn't requested by the caller's token,
                // then it should only be due to a CancelPendingRequests call or the HttpClient.Timeout.
                // Here we cannot differentiate cleanly between the 2 (as opposed to the private HttpClient.HandleFailure) because
                // we cannot access the internals of the finalCancellation token.
                // But we gave the Hook and access to its HttpClient exactly for this: by using the current HttpClient.Timeout
                // value and the started/ended values, we can decide that if the configured timeout has been exhausted.
                //
                // This is an approximation... But failing to have the exact source of cancellation between a call
                // to CancelPendingRequests or a Timeout is not a big deal.
                // 
                var source = HttpRequestCancellationSource.None;
                // First, if this is a HttpRequestException and the final token is signaled, we wrap it
                // in a OperationCanceledException because on cancellation, race conditions abound, and we
                // consider the failure to be caused by the cancellation (e.g. Exception when reading from
                // canceled response stream).
                if( ex is HttpRequestException hEx && finalCancellation.IsCancellationRequested )
                {
                    ex = new OperationCanceledException( $"HttpRequestException ('{hEx.HttpRequestError}') while canceled operation.",
                                                         ex,
                                                         _userCancellation.IsCancellationRequested ? _userCancellation : finalCancellation );
                }
                // Then handle all OperationCanceledException, tries to source them and when possible try to create an
                // exception that looks like the ones created by the private HttpClient.HandleFailure.
                if( ex is OperationCanceledException oce )
                {
                    if( _userCancellation.IsCancellationRequested )
                    {
                        if( oce.CancellationToken != _userCancellation )
                        {
                            // We got a cancellation exception and the user cancellation has been signaled, but the exception doesn't contain
                            // that token.
                            // Ensures that the exception contains the user's token.
                            ex = new TaskCanceledException( oce.Message, oce, _userCancellation );
                        }
                        source = HttpRequestCancellationSource.User;
                    }
                    else 
                    {
                        source = HttpRequestCancellationSource.Other;
                        if( finalCancellation.IsCancellationRequested )
                        {
                            Throw.DebugAssert( "This is part of the Hook initialization.", _httpClient != null );
                            if( _httpClient.Timeout >= TimeSpan.FromMilliseconds( Environment.TickCount64 - _startedTickCount ) )
                            {
                                ex = new TaskCanceledException( $"HttpClient.Timeout '{_httpClient.Timeout}'.", new TimeoutException( ex.Message, ex ), oce.CancellationToken );
                                source = HttpRequestCancellationSource.Timeout;
                            }
                            else if( oce.CancellationToken == finalCancellation )
                            {
                                source = HttpRequestCancellationSource.CancelRequests;
                            }
                        }
                    }
                }
                return HttpExceptionContent.CreateResponseMessage( request, ex, source );
            }
        }
    }

}

