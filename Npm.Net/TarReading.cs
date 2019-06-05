using CK.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Npm.Net
{
    public static class TarReader
    {

        static readonly Regex _packageJson = new Regex( @"^package\/package\.json$" );
        static readonly Regex _authors = new Regex( @"^package\/AUTHORS$" );
        static readonly Regex _serverjs = new Regex( @"^package\/server\.js$" );
        static readonly Regex _readme = new Regex( @"^package\/README\.?[^\/]*$" );
        static readonly Regex _gypFiles = new Regex( @"^package\/[^\/]*\.gyp$" );
        static readonly Regex _man = new Regex( @".[0-9]$" );
        static readonly Regex _bin = new Regex( @"^[^.].*" );
        public static JObject ExtractPackageJson( IActivityMonitor m, MemoryStream tarball )
        {
            var output = ExtractFiles( m, tarball, new List<Regex> { _packageJson } );
            if( output.Count == 0 )
            {
                m.Error( "No package.json in the tarbal." );
                return null;
            }
            return JObject.Parse( output[_packageJson].Single().content );
        }

        public static JObject ExtractModifiedPackageJson( IActivityMonitor m, MemoryStream tarball )
        {

            Dictionary<Regex, List<(string fileName, string content)>> output = ExtractFiles( m, tarball, new List<Regex> { _packageJson, _authors, _readme, _gypFiles, _serverjs } );
            JObject packageJson = JObject.Parse( output[_packageJson].Single().content );

            if( output.ContainsKey( _authors ) ) ApplyAuthors( packageJson, output[_authors] );
            if( output.ContainsKey( _gypFiles ) ) ApplyGyp( packageJson, output[_gypFiles] );
            if( output.ContainsKey( _readme ) ) ApplyReadme( m, packageJson, output[_readme] );
            if( output.ContainsKey( _serverjs ) ) ApplyServerJS( m, packageJson );
            ApplyMan( packageJson, output.ContainsKey(_man) ? output[_man] : null );
            if( output.ContainsKey( _bin ) ) ApplyBin( packageJson, output[_bin] );
            if( packageJson["author"] != null )
            {
                //convert the author entry to an object entry.
                packageJson["author"] = Author.FromString( packageJson["author"].ToString() ).ToJObject();
            }
            if( packageJson["scripts"] != null )
            {
                foreach( var script in packageJson["scripts"].Values<JProperty>() )
                {
                    string scriptString = script.Value.ToString();
                    script.Value = Regex.Replace( scriptString, @"/^(\.[/\\])?node_modules[/\\].bin[\\/]/", "" );
                }
            }

            if( packageJson["gitHead"] != null )
            {
                m.Warn( "The 'gitHead' attribute should not be in the package.json !" );
            }
            var repo = packageJson["repository"];
            if(repo != null && repo["type"]?.ToString() == "git")
            {
                string url = packageJson["repository"]["url"]?.ToString();
                if(!url.StartsWith("git+"))
                {
                    url = "git+" + url;
                }
                if(!url.EndsWith(".git"))
                {
                    url += ".git";
                }
                packageJson["repository"]["url"] = url;
            }

            packageJson["maintainers"] = new JArray()
            {
                new JObject()
            };

            return packageJson;
        }

        static void ApplyBin( JObject packageJson, List<(string fileName, string content)> matchedFiles )
        {
            JToken binDir = packageJson["directories"]?["bin"];
            if( packageJson["bin"] != null || binDir == null ) return;
            matchedFiles.Where( p => Regex.IsMatch( p.fileName, $@"/^package\/{binDir}\/[^\/]*.[0-9]$" ) );
            packageJson["bin"] = new JArray( matchedFiles.Select( p => "." + p.fileName.Substring( 7 ) ) );
        }

        static void ApplyMan( JObject packageJson, List<(string fileName, string content)> matchedFiles )
        {
            JToken manDir = packageJson["directories"]?["man"];
            if( packageJson["man"] != null || manDir == null ) return;
            if(matchedFiles == null)
            {
                packageJson["man"] = new JArray();
                return;
            }
            matchedFiles.Where( p => Regex.IsMatch( p.fileName, $@"/^package\/{manDir}\/[^\/]*.[0-9]$" ) );
            packageJson["man"] = new JArray( matchedFiles.Select( p => "." + p.fileName.Substring( 7 ) ) );
        }

        static void ApplyServerJS( IActivityMonitor m, JObject packageJson )
        {
            if( packageJson["scripts"]?["start"] != null ) return;
            if( packageJson["scripts"] == null )
            {
                packageJson["scripts"] = new JObject();
                m.Debug( "Adding scripts object in package.json" );
            }
            packageJson["scripts"]["start"] = "node server.js";
        }
        static void ApplyReadme( IActivityMonitor m, JObject packageJson, List<(string fileName, string content)> matchedFiles )
        {
            if( packageJson["readme"] != null ) return;
            Regex markdownRegex = new Regex( @"\.m?a?r?k?d?o?w?n?$" );
            var markdown = matchedFiles.FirstOrDefault( p => markdownRegex.IsMatch( p.fileName ) );
            if( markdown.fileName == null ) markdown = matchedFiles.FirstOrDefault( p => p.fileName == "package/README" );
            if( markdown.fileName == null )
            {
                m.Error( "Faulty regex, this is a bug, skipping readme." );
                return;
            }
            packageJson["readme"] = markdown.content;
            packageJson["readmeFilename"] = markdown.fileName.Substring(8, markdown.fileName.Length-8);
        }
        static void ApplyGyp( JObject packageJson, List<(string fileName, string content)> matchedFiles )
        {
            if( packageJson["scripts"]?["install"] != null || packageJson["scripts"]?["preinstall"] != null ) return;
            if( packageJson["scripts"] == null ) packageJson["scripts"] = new JObject();
            if( matchedFiles.Any() )
            {
                packageJson["scripts"]["install"] = "node-gyp rebuild";
                packageJson["gypfile"] = true;
            }
        }

        static void ApplyAuthors( JObject packageJson, List<(string fileName, string content)> matchedFiles )
        {
            if( packageJson["contributors"] == null )
            {
                var authorsList = Author.FromFile(
                    matchedFiles.Single().content.Split( "\r\n".ToArray(), StringSplitOptions.RemoveEmptyEntries )
                ).Select( p => p.ToJObject() );
                packageJson["contributors"] = new JArray( authorsList );
            }
        }

        public static Dictionary<Regex, List<(string fileName, string content)>> ExtractFiles( IActivityMonitor m, MemoryStream tarball, List<Regex> watchers )
        {
            Dictionary<Regex, List<(string fileName, string content)>> output = new Dictionary<Regex, List<(string fileName, string content)>>();
            var buffer = new byte[100];
            while( true )
            {
                tarball.Read( buffer, 0, 100 );
                var name = Encoding.ASCII.GetString( buffer ).Trim( ' ', '\0' );
                if( string.IsNullOrWhiteSpace( name ) )
                {
                    m.Info( $"Tar entries {string.Join( ", ", watchers.Where( p => !output.ContainsKey( p ) ) )} not found(s)." );
                    return output;
                }
                tarball.Seek( 24, SeekOrigin.Current );
                tarball.Read( buffer, 0, 12 );
                var size = Convert.ToInt64( Encoding.ASCII.GetString( buffer, 0, 12 ).Trim( ' ', '\0' ), 8 );
                tarball.Seek( 376L, SeekOrigin.Current );
                string file = null;
                foreach( var regex in watchers )
                {
                    if( regex.IsMatch( name ) )
                    {
                        if( file == null )
                        {
                            m.Info( $"Found {name} in tarball." );
                            var bytes = new Byte[size];
                            tarball.Read( bytes, 0, bytes.Length );
                            file = Encoding.UTF8.GetString( bytes );
                        }
                        if( !output.ContainsKey( regex ) ) output[regex] = new List<(string fileName, string content)>();
                        output[regex].Add( (name, file) );
                    }
                }
                if( file == null )
                {
                    tarball.Seek( size, SeekOrigin.Current );
                }
                var pos = tarball.Position;
                var offset = 512 - (pos % 512);
                if( offset == 512 ) offset = 0;
                tarball.Seek( offset, SeekOrigin.Current );
            }

        }
    }

}
