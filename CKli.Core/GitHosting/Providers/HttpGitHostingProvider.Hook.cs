using CK.Core;
using CKli.Core.GitHosting.Models.GitHub;
using System;
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

        internal Hook( HttpGitHostingProvider provider, IActivityMonitor monitor, RetryHelper retryHelper )
            : base( provider._handler )
        {
            _provider = provider;
            _monitor = monitor;
            _retryHelper = retryHelper;
        }

        protected override HttpResponseMessage Send( HttpRequestMessage request, CancellationToken cancellationToken )
        {
            throw new NotSupportedException( "GitHostingProvider API is only async." );
        }

        protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
        {
            return _provider.OnSendHookAsync( _monitor, request, DoSendAsync, _retryHelper, cancellationToken );
        }

        async Task<HttpResponseMessage> DoSendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
        {
            try
            {
                return await base.SendAsync( request, cancellationToken ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                _monitor.Error( $"""
                    While sending request:
                    {request}
                    """, ex );
                return HttpExceptionContent.CreateResponseMessage( request, ex );
            }
        }
    }
}
