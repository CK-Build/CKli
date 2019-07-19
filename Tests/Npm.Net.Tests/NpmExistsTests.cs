using CK.Env.NPM;
using CK.Env.Tests;
using CSemVer;
using FluentAssertions;
using NUnit.Framework;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
namespace Npm.Net.Tests
{
    public class NpmExistsTests
    {
        readonly Registry _registry;
        public NpmExistsTests()
        {
            _registry = new Registry( TestHelperHttpClient.HttpClient );
        }

        [Test]
        public async Task ViewWithVersionReturnValidJson()
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
