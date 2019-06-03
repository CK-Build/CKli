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
    public class MetadataStream : HttpContent, IDisposable
    {
        const string _tarballContentPlaceholderString = "TARBALL_DATA_TO_REPLACE";
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

        MetadataStream( IActivityMonitor m, MergeStream mergeStream, string lastPart, Base64StreamLength tarballStream, SHA512Stream sHA512Stream, SHA1Stream sHA1Stream )
        {
            _m = m ?? throw new NullReferenceException();
            _mergeStream = mergeStream ?? throw new NullReferenceException();
            _lastPart = lastPart;
            _lastPartStream = new MemoryStream( Encoding.UTF8.GetBytes( _lastPart ) );
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
            string[] splittedJson = json.Split( new string[] { _tarballContentPlaceholderString }, StringSplitOptions.None );
            //We split the json in half where the content should go.
            if( splittedJson.Length != 2 ) throw new InvalidOperationException( "The author of this method is stupid" );
            var mergeStream = new MergeStream();

            mergeStream.AddStream( new MemoryStream( Encoding.UTF8.GetBytes( splittedJson[0] ) ) );//We send the first half of the json.
            SHA1Stream sHA1Stream = new SHA1Stream( tarball, true, false );
            SHA512Stream sHA512Stream = new SHA512Stream( sHA1Stream, true, false );
            var tarball64 = new Base64StreamLength( tarball.Length, sHA512Stream );
            mergeStream.AddStream( tarball64 ); //Then the tarball

            string lengthString = tarball64.Length.ToString(); //Now, we know the length of the tarball
            m.Info( $"Replacing length placeholder by {lengthString}." );
            string lastJsonPart = splittedJson[1];
            lastJsonPart = lastJsonPart.Replace( _tarballLengthString, lengthString ); //so we put the length in the last part of the json.
            var output = new MetadataStream( m, mergeStream, lastJsonPart, tarball64, sHA512Stream, sHA1Stream );
            mergeStream.OnNextStream += output.MergeStream_OnNextStream;//This allow us to write the hash later.
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
                _mergeStream.AddStream( new MemoryStream( Encoding.UTF8.GetBytes( replaced ) ) );
            }
        }

        static JObject LegacyMetadataJson( IActivityMonitor m, Uri registryUri, JObject packageJson, Stream tarball, string distTag )
        {
            string checkJson = packageJson.ToString();
            if( checkJson.Contains( _tarballContentPlaceholderString ) ) throw new ArgumentException( "Please don't put the string 'TARBALL_DATA_TO_REPLACE' in the packageJson" );
            if( checkJson.Contains( _tarballLengthString ) ) throw new ArgumentException( "Please don't put the string 'TARBALL_LENGTH_TO_REPLACE' in the packageJson" );
            m.Debug( $"Placeholder sha512:{_tarballSha512String}." );
            m.Debug( $"Placeholder sha1:{_tarballSha1String}." );
            tarball.Position = 0;
            m.Debug( "Stream position set to 0." );
            string name = packageJson["name"] + "@" + packageJson["version"];
            packageJson["_id"] = name;
            m.Info( $"Package name:'{name}'." );
            string tbName = packageJson["name"] + "-" + packageJson["version"] + ".tgz";
            string tbUri = new Uri( registryUri, packageJson["name"] + "/-/" + tbName ).ToString().Replace( "https", "http" );
            m.Debug( $"legacy tarball uri: {tbUri}." );
            JObject distTags = new JObject();
            if( distTag != null )
            {
                distTags[distTag] = packageJson["version"];
            }

            JObject output = new JObject
            {
                ["_id"] = packageJson["name"],
                ["name"] = packageJson["name"],
                ["description"] = packageJson["name"],
                ["dist-tags"] = distTags,
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
                        ["data"] = _tarballContentPlaceholderString,
                        ["length"] = _tarballLengthString
                    }
                },
                ["dist"] = new JObject()
                {
                    ["integrity"] = _tarballSha512String,
                    ["shasum"] = _tarballSha1String,
                    ["tarball"] = tbUri
                }
            };
            return output;
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
