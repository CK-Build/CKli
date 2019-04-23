using CK.Core;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Npm.Net
{
    class MetadataStream : HttpContent, IDisposable
    {
        const string _tarballContentString = "TARBALL_DATA_TO_REPLACE";
        const string _tarballLengthString = "TARBALL_LENGTH_TO_REPLACE";
        const string _tarballSha512String = "sha512-00000000000000000000000000000000000000000000000000000000000000000000000000000000000000==";
        const string _tarballSha1String = "111111111111111111111111111111111111111";
        readonly IActivityMonitor _m;
        readonly MergeStream _mergeStream;
        readonly SHA512Stream _sHA512Stream;
        readonly SHA1Stream _sHA1Stream;
        readonly string _lastPart;
        readonly Stream _tarballStream;
        readonly Stream _lastPartStream;

        MetadataStream( IActivityMonitor m, MergeStream mergeStream, string lastPart, Stream tarballStream, SHA512Stream sHA512Stream, SHA1Stream sHA1Stream )
        {
            _m = m ?? throw new NullReferenceException();
            _mergeStream = mergeStream ?? throw new NullReferenceException();
            _lastPart = lastPart;
            _lastPartStream = new MemoryStream( Encoding.UTF8.GetBytes( _lastPart ) );
            mergeStream.AddStream( _lastPartStream );
            _tarballStream = tarballStream;
            _sHA512Stream = sHA512Stream;
            _sHA1Stream = sHA1Stream;
            Headers.Add( "content-type", "application/json" );
        }

        public static MetadataStream LegacyMetadataStream(
            IActivityMonitor m,
            Uri registryUri,
            JObject packageJson,
            Stream tarball,
            string distTag = null )
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
            mergeStream.AddStream( new MemoryStream( Encoding.UTF8.GetBytes( splittedJson[0] ) ) );
            var tarball64 = new Base64StreamLength( tarball );
            mergeStream.AddStream( tarball64 );
            string lengthString = tarball64.Length.ToString();
            m.Info( $"Replacing length placeholder by {lengthString}." );
            lastJsonPart = lastJsonPart.Replace( _tarballLengthString, lengthString );
            SHA1Stream sHA1Stream = new SHA1Stream( tarball, true, false );
            SHA512Stream sHA512Stream = new SHA512Stream( sHA1Stream , true, false);
            var output = new MetadataStream( m, mergeStream, lastJsonPart,tarball, sHA512Stream, sHA1Stream );
            var lastPartStream = new MemoryStream( Encoding.UTF8.GetBytes( output._lastPart ) );
            mergeStream.AddStream( lastPartStream );
            return output;
        }

        void MergeStream_OnNextStream( object sender, Stream e )
        {
            if( e == _tarballStream )
            {
                var sha1 = _sHA1Stream.GetFinalResult();
                var sha512 = _sHA512Stream.GetFinalResult();
                string replaced = _lastPart.Replace( _tarballSha1String, sha1.ToString() );
                replaced = replaced.Replace( _tarballSha512String, "sha512-" + Convert.ToBase64String( sha512.GetBytes().ToArray() ) );
                _mergeStream.ReplaceStream( _lastPartStream, new MemoryStream( Encoding.UTF8.GetBytes( replaced ) ));
            }
        }

        static JObject LegacyMetadataJson( IActivityMonitor m, Uri registryUri, JObject packageJson, Stream tarball, string distTag )
        {
            string checkJson = packageJson.ToString();
            if( checkJson.Contains( _tarballContentString ) ) throw new ArgumentException( "Please don't put the string 'TARBALL_DATA_TO_REPLACE' in the packageJson" );
            if( checkJson.Contains( _tarballLengthString ) ) throw new ArgumentException( "Please don't put the string 'TARBALL_LENGTH_TO_REPLACE' in the packageJson" );
            string integrity;
            string shasum;
            integrity = _tarballSha512String;
            m.Debug( $"Placeholder sha512:{integrity}." );
            shasum = _tarballSha1String;
            m.Debug( $"Placeholder sha1:{shasum}." );
            tarball.Position = 0;
            m.Debug( "Stream position set to 0." );
            string name = packageJson["name"] + "@" + packageJson["version"];
            packageJson["_id"] = name;
            m.Info( $"Package name:'{name}'." );
            string tbName = packageJson["name"] + "-" + packageJson["version"] + ".tgz";
            string tbUri = new Uri( registryUri, packageJson["name"] + "/-/" + tbName ).ToString().Replace( "https", "http" );
            m.Debug( $"legacy tarball uri: {tbUri}." );
            packageJson["dist"] = null;
            if( distTag != null )
            {
                packageJson["dist"] = new JObject()
                {
                    ["integrity"] = integrity,
                    ["shasum"] = shasum,
                    ["tarball"] = tbUri
                };
            }
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
                        ["length"] = _tarballLengthString
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
            _mergeStream.Dispose();
        }
    }
}
