using CK.Core;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Npm.Net
{
    public class MetadataStream : HttpContent, IDisposable
    {
        const string _tarballContentString = "TARBALL_DATA_TO_REPLACE";
        const string _tarbalLengthString = "TARBALL_LENGTH_TO_REPLACE";
        readonly IActivityMonitor _m;
        readonly MergeStream _mergeStream;

        MetadataStream( IActivityMonitor m, MergeStream mergeStream )
        {
            _m = m ?? throw new NullReferenceException();
            _mergeStream = mergeStream ?? throw new NullReferenceException();
            Headers.Add( "content-type", "application/json" );
        }

        public static MetadataStream LegacyMetadataStream(
            IActivityMonitor m,
            Uri registryUri,
            JObject packageJson,
            Stream tarball,
            string distTag )
        {
            JObject json = LegacyMetadataJson( m, registryUri, packageJson, tarball, distTag );
            return FromMetadata( m, json, tarball );
        }


        static MetadataStream FromMetadata( IActivityMonitor m, JObject metadata, Stream tarball )
        {
            string json = metadata.ToString();
            string[] splittedJson = json.Split( new string[] { _tarballContentString }, StringSplitOptions.None );
            if( splittedJson.Length != 2 ) throw new InvalidOperationException( "The author of this method is stupid" );
            string lastJsonPart = splittedJson[1];
            var mergeStream = new MergeStream();
            var output = new MetadataStream( m, mergeStream );
            mergeStream.AddStream( new MemoryStream( Encoding.UTF8.GetBytes( splittedJson[0] ) ) );
            var tarball64 = new Base64StreamLength( tarball );
            mergeStream.AddStream( tarball64 );
            string lengthString = tarball64.Length.ToString();
            string lastPart = lastJsonPart.Replace( _tarbalLengthString, lengthString );
            m.Info( $"Replacing length placeholder by {lengthString}." );
            var lastPartStream = new MemoryStream( Encoding.UTF8.GetBytes( lastPart ) );
            mergeStream.AddStream( lastPartStream );
            return output;
        }

        static JObject LegacyMetadataJson( IActivityMonitor m, Uri registryUri, JObject packageJson, Stream tarball, string distTag )
        {
            if( distTag == null ) distTag = "latest";
            if( !tarball.CanSeek ) throw new ArgumentException( "I need two pass on this stream" );
            string checkJson = packageJson.ToString();

            if( checkJson.Contains( _tarballContentString ) ) throw new ArgumentException( "Please don't put the string 'TARBALL_DATA_TO_REPLACE' in the packageJson" );
            if( checkJson.Contains( _tarbalLengthString ) ) throw new ArgumentException( "Please don't put the string 'TARBALL_LENGTH_TO_REPLACE' in the packageJson" );
            string integrity;
            string shasum;
            using( m.OpenInfo( "Calculating tarball checksum." ) )
            {
                var (sha1, sha512) = CalculateIntegrity( tarball );
                integrity = "sha512-" + Convert.ToBase64String( sha512 );
                m.Info( $"sha512:{integrity}." );
                shasum = sha1.ToString();
                m.Info( $"sha1:{shasum}." );
            }
            tarball.Position = 0;
            m.Debug( "Stream position set to 0." );
            string name = packageJson["name"] + "@" + packageJson["version"];
            packageJson["_id"] = name;
            m.Info( $"Package name:'{name}'." );
            string tbName = packageJson["name"] + "-" + packageJson["version"] + ".tgz";
            string tbUri = new Uri( registryUri, packageJson["name"] + "/-/" + tbName ).ToString().Replace( "https", "http" );
            m.Debug( $"legacy tarball uri: {tbUri}." );
            packageJson["dist"] = new JObject()
            {
                ["integrity"] = integrity,
                ["shasum"] = shasum,
                ["tarball"] = tbUri
            };
            return new JObject()
            {
                ["_id"] = packageJson["name"],
                ["name"] = packageJson["name"],
                ["description"] = packageJson["name"],
                ["dist-tags"] = new JObject
                {
                    [distTag] = packageJson["version"]
                },
                ["versions"] = new JObject
                {
                    [packageJson["version"].ToString()] = packageJson
                },
                ["readme"] = packageJson["readme"] ?? "",
                ["_attachments"] = new JObject
                {
                    [tbName] = new JObject()
                    {
                        ["content_type"] = "application/octet-stream",
                        ["data"] = _tarballContentString,
                        ["length"] = _tarbalLengthString
                    }

                }
            };
        }

        static (SHA1Value sha1, byte[] sha512) CalculateIntegrity( Stream stream )
        {
            using( SHA1Stream shaStream = new SHA1Stream( stream, true, true ) )
            using( SHA512 sha512 = SHA512.Create() )
            {
                byte[] output = sha512.ComputeHash( shaStream );
                return (shaStream.GetFinalResult(), output);
            }
        }

        protected override async Task SerializeToStreamAsync( Stream stream, TransportContext context )
        {
            _m.Info( "Sending request body." );
            await _mergeStream.CopyToAsync( stream );
        }

        protected override bool TryComputeLength( out long length )
        {
            _m.Info( $"Body length: {_mergeStream.Length} ." );
            length = _mergeStream.Length;
            return true;
        }

        protected override void Dispose( bool disposing )
        {
            base.Dispose( disposing );
            _mergeStream.Dispose();
        }
    }
}
