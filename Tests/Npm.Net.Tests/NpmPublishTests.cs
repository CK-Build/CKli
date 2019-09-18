using CK.Env.NPM;
using CK.Env.Tests;
using FluentAssertions;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;
namespace Npm.Net.Tests
{
    public class NpmPublishTests
    {
        [Test]
        [Explicit]
        public void PublishOnNpm()
        {
            string pat = "";
            var registry = new Registry( TestHelperHttpClient.HttpClient, pat );
            bool success = registry.Publish( TestHelper.Monitor, "testpackagethatnooneshoulduse-6.42.5-ci.tgz", true, "dist-tag-test" );
            success.Should().BeTrue();
        }
    }
}
