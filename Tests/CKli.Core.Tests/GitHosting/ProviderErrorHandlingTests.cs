using CK.Core;
using CKli.Core.GitHosting;
using CKli.Core.GitHosting.Providers;
using CKli.Core.Tests.GitHosting.Mocks;
using NUnit.Framework;
using Shouldly;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace CKli.Core.Tests.GitHosting;

/// <summary>
/// Tests for error handling scenarios across all Git hosting providers.
/// Covers server errors, malformed responses, and edge cases.
/// </summary>
[TestFixture]
public class ProviderErrorHandlingTests
{
    static readonly Uri TestApiUrl = new( "https://test.example.com/api/v1/" );

    #region Server Error (5xx) Tests

    [Test]
    public async Task GitHubProvider_handles_500_server_errorAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueResponse( HttpStatusCode.InternalServerError, "Internal Server Error" );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

        result.Success.ShouldBeFalse();
        result.HttpStatusCode.ShouldBe( 500 );
    }

    [Test]
    public async Task GitLabProvider_handles_502_bad_gatewayAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueResponse( HttpStatusCode.BadGateway, "Bad Gateway" );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "group", "repo" );

        result.Success.ShouldBeFalse();
        result.HttpStatusCode.ShouldBe( 502 );
    }

    [Test]
    public async Task GiteaProvider_handles_503_service_unavailableAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueResponse( HttpStatusCode.ServiceUnavailable, "Service Unavailable" );

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

        result.Success.ShouldBeFalse();
        result.HttpStatusCode.ShouldBe( 503 );
    }

    #endregion

    #region Malformed Response Tests

    [Test]
    public async Task GitHubProvider_handles_empty_responseAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( "" );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

        result.Success.ShouldBeFalse();
    }

    [Test]
    public async Task GitLabProvider_handles_invalid_jsonAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( "{ invalid json }" );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "group", "repo" );

        result.Success.ShouldBeFalse();
    }

    [Test]
    public async Task GiteaProvider_handles_html_instead_of_jsonAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( "<html><body>Error</body></html>" );

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

        result.Success.ShouldBeFalse();
    }

    #endregion

    #region HTTP Status Edge Cases

    [TestCase( 400, Description = "Bad Request" )]
    [TestCase( 408, Description = "Request Timeout" )]
    [TestCase( 422, Description = "Unprocessable Entity" )]
    public async Task GitHubProvider_handles_various_client_errorsAsync( int statusCode )
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueResponse( (HttpStatusCode)statusCode, $"Error {statusCode}" );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

        result.Success.ShouldBeFalse();
        result.HttpStatusCode.ShouldBe( statusCode );
        result.IsAuthenticationError.ShouldBeFalse();
        result.IsNotFound.ShouldBeFalse();
        result.IsRateLimited.ShouldBeFalse();
    }

    [Test]
    public async Task Provider_handles_403_as_auth_errorAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueForbiddenResponse( "Access denied" );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

        result.Success.ShouldBeFalse();
        result.IsAuthenticationError.ShouldBeTrue();
    }

    #endregion
}
