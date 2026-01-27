using CKli.Core.GitHosting;
using NUnit.Framework;
using Shouldly;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class GitHostingOperationResultTests
{
    [Test]
    public void Ok_creates_successful_result()
    {
        var result = GitHostingOperationResult.Ok();

        result.Success.ShouldBeTrue();
        result.ErrorMessage.ShouldBeNull();
        result.HttpStatusCode.ShouldBeNull();
    }

    [Test]
    public void Fail_with_message_creates_failed_result()
    {
        var result = GitHostingOperationResult.Fail( "Something went wrong" );

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe( "Something went wrong" );
        result.HttpStatusCode.ShouldBeNull();
    }

    [Test]
    public void Fail_with_status_code_creates_failed_result()
    {
        var result = GitHostingOperationResult.Fail( "Not found", 404 );

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe( "Not found" );
        result.HttpStatusCode.ShouldBe( 404 );
    }

    [TestCase( 401 )]
    [TestCase( 403 )]
    public void IsAuthenticationError_returns_true_for_auth_errors( int statusCode )
    {
        var result = GitHostingOperationResult.Fail( "Auth error", statusCode );
        result.IsAuthenticationError.ShouldBeTrue();
    }

    [TestCase( 200 )]
    [TestCase( 404 )]
    [TestCase( 500 )]
    public void IsAuthenticationError_returns_false_for_non_auth_errors( int statusCode )
    {
        var result = GitHostingOperationResult.Fail( "Error", statusCode );
        result.IsAuthenticationError.ShouldBeFalse();
    }

    [Test]
    public void IsNotFound_returns_true_for_404()
    {
        var result = GitHostingOperationResult.Fail( "Not found", 404 );
        result.IsNotFound.ShouldBeTrue();
    }

    [TestCase( 200 )]
    [TestCase( 401 )]
    [TestCase( 500 )]
    public void IsNotFound_returns_false_for_non_404( int statusCode )
    {
        var result = GitHostingOperationResult.Fail( "Error", statusCode );
        result.IsNotFound.ShouldBeFalse();
    }

    [Test]
    public void IsRateLimited_returns_true_for_429()
    {
        var result = GitHostingOperationResult.Fail( "Rate limited", 429 );
        result.IsRateLimited.ShouldBeTrue();
    }

    [Test]
    public void Generic_Ok_creates_successful_result_with_data()
    {
        var data = new RepositoryInfo
        {
            Owner = "owner",
            Name = "repo"
        };

        var result = GitHostingOperationResult<RepositoryInfo>.Ok( data );

        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Owner.ShouldBe( "owner" );
        result.Data!.Name.ShouldBe( "repo" );
        result.ErrorMessage.ShouldBeNull();
    }

    [Test]
    public void Generic_Fail_creates_failed_result_without_data()
    {
        var result = GitHostingOperationResult<RepositoryInfo>.Fail( "Error", 500 );

        result.Success.ShouldBeFalse();
        result.Data.ShouldBeNull();
        result.ErrorMessage.ShouldBe( "Error" );
        result.HttpStatusCode.ShouldBe( 500 );
    }
}
