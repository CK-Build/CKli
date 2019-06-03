using CK.Core;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Npm.Net;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Tests
{
    public class Tests
    {

        [Test]
        public async Task CanParseGeneratedJsonCorrectly()
        {
            using( var tarFile = File.OpenRead( "signature-json-graph-serializer-0.0.4.tgz" ) )
            {
                ActivityMonitor m = new ActivityMonitor();

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
                var packageJson = Registry.ExtractPackageJson( m, tarball );
                packageJson.Should().NotBeNull();
                var metadataStream = MetadataStream.LegacyMetadataStream( m, new Uri( "https://Registry.Uri" ), packageJson, tarFile );
                long? length = metadataStream.Headers.ContentLength;
                length.Should().NotBeNull();
                string result = await metadataStream.ReadAsStringAsync();
                result.Length.Should().Equals( (int)length.Value );
                var json = JObject.Parse( result );//Throw if we generated bad json.
                var dist = json["dist"];
                dist["shasum"].ToString().Should().Be( sHA1Value.ToString() );
                dist["integrity"].ToString().Should().Be( "sha512-" + Convert.ToBase64String( sHA512Value.GetBytes().ToArray() ) );
            }
        }
    }
}
