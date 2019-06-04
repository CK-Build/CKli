using CK.Core;
using CK.Env.Tests;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Npm.Net.Tests
{
    public class NpmPublishTests
    {

        [Test]
        public async Task CanParseGeneratedJsonCorrectly()
        {
            using( var tarFile = File.OpenRead( "signature-json-graph-serializer-0.0.4.tgz" ) )
            {
                var tarball = new MemoryStream();
                SHA1Value sHA1Value;
                SHA512Value sHA512Value;
                using( SHA1Stream sHA1Stream = new SHA1Stream( tarFile, true, true ) )
                using( SHA512Stream sHA512Stream = new SHA512Stream( sHA1Stream, true, true ) )
                using( GZipStream dezipped = new GZipStream( tarFile, CompressionMode.Decompress, true ) )
                {
                    dezipped.CopyTo( tarball );
                    sHA1Value = sHA1Stream.GetFinalResult();
                    sHA512Value = sHA512Stream.GetFinalResult();
                }
                tarball.Position = 0;
                tarFile.Position = 0;
                var packageJson = Registry.ExtractPackageJson( TestHelper.Monitor, tarball );
                packageJson.Should().NotBeNull();
                var metadataStream = MetadataStream.LegacyMetadataStream( TestHelper.Monitor, new Uri( "https://Registry.Uri" ), packageJson, tarFile, "test" );
                long? length = metadataStream.Headers.ContentLength;
                length.Should().NotBeNull();
                string result = await metadataStream.ReadAsStringAsync();
                result.Length.Should().Equals( (int)length.Value );
                var json = JObject.Parse( result );//Throw if we generated bad json.
            }
        }
        [Test]
        public async Task PublishOnNpm()
        {
            string pat = "";
            var registry = new Registry( TestHelperHttpClient.HttpClient, pat );
            using( FileStream stream = File.OpenRead( "testpackagethatnooneshoulduse-6.42.5-ci.tgz" ) )
            {
                bool success = await registry.PublishAsync( TestHelper.Monitor, stream, "dist-tag-test" );
                success.Should().BeTrue();
            }
        }
    }
}
