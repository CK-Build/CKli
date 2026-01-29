//using CK.Core;
//using CKli.Core.GitHosting;
//using NUnit.Framework;
//using Shouldly;
//using System.Threading.Tasks;

//namespace CKli.Core.Tests.GitHosting;

///// <summary>
///// Tests for ProviderDetector behavior with unknown hosts that don't match
///// any well-known hostname or pattern (stages 3 and 4 of detection).
///// </summary>
//[TestFixture]
//public class ProviderDetectorUnknownHostTests
//{
//    sealed class NoCredentialsStore : ISecretsStore
//    {
//        public string? TryGetRequiredSecret( IActivityMonitor monitor, System.Collections.Generic.IEnumerable<string> keys ) => null;
//    }

//    readonly ISecretsStore _noCredentials = new NoCredentialsStore();

//    [Test]
//    public async Task ResolveProviderAsync_unknown_host_returns_null_without_credentialsAsync()
//    {
//        // Host "git.custom-server.com" doesn't match github/gitlab/gitea patterns
//        // Without credentials, API sniffing will fail and try-all can't authenticate
//        var monitor = new ActivityMonitor();

//        var provider = await GitHostingProviderDetector.ResolveProviderAsync(
//            monitor,
//            _noCredentials,
//            "https://git.custom-server.com/owner/repo" );

//        // Should return null since we can't detect without credentials
//        provider.ShouldBeNull();
//    }

//    [Test]
//    public async Task ResolveProviderAsync_unknown_host_with_port_returns_null_without_credentialsAsync()
//    {
//        var monitor = new ActivityMonitor();

//        var provider = await GitHostingProviderDetector.ResolveProviderAsync(
//            monitor,
//            _noCredentials,
//            "https://git.internal:8443/team/project" );

//        provider.ShouldBeNull();
//    }

//    [Test]
//    public async Task ResolveProviderAsync_ssh_unknown_host_returns_null_without_credentialsAsync()
//    {
//        var monitor = new ActivityMonitor();

//        var provider = await GitHostingProviderDetector.ResolveProviderAsync(
//            monitor,
//            _noCredentials,
//            "git@git.custom-server.com:owner/repo.git" );

//        provider.ShouldBeNull();
//    }

//    [Test]
//    public async Task ResolveProviderAsync_empty_string_returns_nullAsync()
//    {
//        var monitor = new ActivityMonitor();

//        var provider = await GitHostingProviderDetector.ResolveProviderAsync(
//            monitor,
//            _noCredentials,
//            "" );

//        provider.ShouldBeNull();
//    }

//    [Test]
//    public async Task ResolveProviderAsync_whitespace_returns_nullAsync()
//    {
//        var monitor = new ActivityMonitor();

//        var provider = await GitHostingProviderDetector.ResolveProviderAsync(
//            monitor,
//            _noCredentials,
//            "   " );

//        provider.ShouldBeNull();
//    }

//    [Test]
//    public void CreateProvider_with_empty_host_throws()
//    {
//        // Empty host causes UriFormatException when building API URL
//        Should.Throw<System.UriFormatException>( () =>
//            GitHostingProviderDetector.CreateProvider( "github", "", _noCredentials ) );
//    }

//    [Test]
//    public void CreateProvider_with_whitespace_host_throws()
//    {
//        // Whitespace host causes UriFormatException when building API URL
//        Should.Throw<System.UriFormatException>( () =>
//            GitHostingProviderDetector.CreateProvider( "gitlab", "   ", _noCredentials ) );
//    }
//}
