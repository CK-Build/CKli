using CK.Env.NPM;
using CK.Env.Tests;
using CSemVer;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Diagnostics;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
namespace Npm.Net.Tests
{
    public class NpmViewTests
    {
        readonly Registry _registry;
        public NpmViewTests()
        {
            _registry = new Registry( TestHelperHttpClient.HttpClient, Registry.NPMJSOrgUri );
        }

        [Test]
        public async Task CreateViewDocumentAndVersionsElementAsync_works_Async()
        {
            var (doc, versions) = await _registry.CreateViewDocumentAndVersionsElementAsync( TestHelper.Monitor, "@signature/json-graph-serializer", true );
            Debug.Assert( doc != null );
            versions.ValueKind.Should().Be( System.Text.Json.JsonValueKind.Object );
            versions.TryGetProperty( "0.0.1", out var v ).Should().BeTrue();
            v.TryGetProperty( "version", out var dup ).Should().BeTrue();
            dup.ValueEquals( "0.0.1" ).Should().BeTrue();

            v.TryGetProperty( "name", out var name ).Should().BeTrue();
            name.ValueEquals( "@signature/json-graph-serializer" ).Should().BeTrue();
            doc.Dispose();
        }

        [Test]
        public async Task ExistAsync_works_Async()
        {
            bool exist = await _registry.ExistAsync( TestHelper.Monitor, "@signature/json-graph-serializer", SVersion.Create( 0, 0, 1 ) );
            exist.Should().BeTrue();
            bool notExist = await _registry.ExistAsync( TestHelper.Monitor, "@signature/packageThatDoesntExist", SVersion.Create( 0, 0, 1 ) );
            notExist.Should().BeFalse();
            bool versionNotExist = await _registry.ExistAsync( TestHelper.Monitor, "@signature/json-graph-serializer", SVersion.Create( 0, 0, 0 ) );
            versionNotExist.Should().BeFalse();
        }

    }
}
