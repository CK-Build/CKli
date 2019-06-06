using CK.Core;
using CK.Env.Tests;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
namespace Npm.Net.Tests
{
    public class NpmPublishTests
    {
        [Test]
        public void PublishOnNpm()
        {
            string pat = "";
            var registry = new Registry( TestHelperHttpClient.HttpClient, pat );
            bool success = registry.Publish( TestHelper.Monitor, "testpackagethatnooneshoulduse-6.42.5-ci.tgz", true, "dist-tag-test" );
            success.Should().BeTrue();
        }
    }
}
