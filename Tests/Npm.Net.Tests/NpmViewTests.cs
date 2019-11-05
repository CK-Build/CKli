using CK.Env.NPM;
using CK.Env.Tests;
using CSemVer;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
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
        public async Task ViewWorkCorrectly()
        {
            (string viewJson, bool exist) = await _registry.View( TestHelper.Monitor, "@signature/json-graph-serializer" );
            JObject json = JObject.Parse( viewJson );
            exist.Should().BeTrue();
            json["versions"]["0.0.1"]["version"].ToString().Should().Be( "0.0.1" );
            json["versions"]["0.0.1"]["name"].ToString().Should().Be( "@signature/json-graph-serializer" );
        }

        [Test]
        public async Task ViewWithVersionReturnValidJson()
        {
            (string viewJson, bool exist) = await _registry.View( TestHelper.Monitor, "@signature/json-graph-serializer", SVersion.Create( 0, 0, 1 ) );
            exist.Should().BeTrue();
            JObject json = JObject.Parse( viewJson );
            json["version"].ToString().Should().Be( "0.0.1" );
            json["name"].ToString().Should().Be( "@signature/json-graph-serializer" );
        }


    }
}
