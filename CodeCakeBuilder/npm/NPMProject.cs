using Cake.Npm;
using CK.Text;
using CodeCake.Abstractions;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodeCake
{
    /// <summary>
    /// Basic (unpublished) NPM project.
    /// </summary>
    public class NPMProject
    {
        class PackageVersionReplacer : IDisposable
        {
            readonly NPMProject _p;
            readonly string _savedPackageJson;

            public PackageVersionReplacer( NPMProject p, SVersion version )
            {
                _p = p;
                _savedPackageJson = File.ReadAllText( p.PackageJson.JsonFilePath );

                // Replace token by SafeSemVersion
                JObject json = JObject.Parse( _savedPackageJson );
                json["version"] = version.ToNuGetPackageString();
                File.WriteAllText( p.PackageJson.JsonFilePath, json.ToString() );
            }

            public void Dispose()
            {
                File.WriteAllText( _p.PackageJson.JsonFilePath.Path, _savedPackageJson );
            }
        }

        public readonly struct SimplePackageJsonFile
        {
            public readonly NormalizedPath JsonFilePath;
            public readonly string Name;
            public readonly string Version;
            public readonly IReadOnlyList<string> Scripts;

            public SimplePackageJsonFile( NormalizedPath folderPath )
            {
                JsonFilePath = folderPath.AppendPart( "package.json" );
                JObject json = JObject.Parse( JsonFilePath );
                Name = json.Value<string>( "name" );
                Version = json.Value<string>( "version" );

                if( json.TryGetValue( "scripts", out JToken scriptsToken ) && scriptsToken.HasValues )
                {
                    Scripts = scriptsToken.Children<JProperty>().Select( p => p.Name ).ToArray();
                }
                else
                {
                    Scripts = Array.Empty<string>();
                }
            }
        }

        public NPMProject( NormalizedPath path )
            : this( path, new SimplePackageJsonFile( path ) )
        {
        }

        protected NPMProject( NormalizedPath path, SimplePackageJsonFile json )
        {
            DirectoryPath = path;
            PackageJson = json;
            NPMRCPath = path.AppendPart( ".npmrc" );
        }

        public virtual bool IsPublished => false;

        public NormalizedPath DirectoryPath { get; }

        public SimplePackageJsonFile PackageJson { get; }

        public NormalizedPath NPMRCPath { get; }

        public virtual void RunInstall( StandardGlobalInfo globalInfo )
        {
            globalInfo.Cake.NpmInstall( new Cake.Npm.Install.NpmInstallSettings()
            {
                LogLevel = NpmLogLevel.Info,
                WorkingDirectory = DirectoryPath.Path
            } );
        }

        public virtual void RunInstallAndClean( StandardGlobalInfo globalInfo, string cleanScriptName = "clean" )
        {
            RunInstall( globalInfo );
            globalInfo.Cake.NpmRunScript(
                    cleanScriptName,
                    s => s
                        .WithLogLevel( NpmLogLevel.Info )
                        .FromPath( DirectoryPath.Path )
                );
        }

        /// <summary>
        /// Runs the 'build-debug', 'build-release' or 'build' script.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        public virtual void RunBuild( StandardGlobalInfo globalInfo )
        {
            string name = globalInfo.IsRelease && PackageJson.Scripts.Contains( "build-debug" )
                            ? "build-debug"
                            : (!globalInfo.IsRelease && PackageJson.Scripts.Contains( "build-release" )
                                ? "build-release"
                                : "build");

            globalInfo.Cake.NpmRunScript(
                    name,
                    s => s
                        .WithLogLevel( NpmLogLevel.Info )
                        .FromPath( DirectoryPath.Path )
                );
        }

        public virtual void RunTest( StandardGlobalInfo globalInfo )
        {
            globalInfo.Cake.NpmRunScript(
                    "test",
                    s => s
                        .WithLogLevel( NpmLogLevel.Info )
                        .FromPath( DirectoryPath.Path )
                );
        }


        public IDisposable TemporarySetVersion( SVersion version ) => new PackageVersionReplacer( this, version );

        public IDisposable TemporarySetPushTargetAndTokenLogin( string pushUri, string token )
        {
            return NPMRCTokenInjector.TokenLogin( pushUri, token, NPMRCPath );
        }

        public IDisposable TemporarySetPushTargetAndAzurePatLogin( string pushUri, string pat )
        {
            var pwd = Convert.ToBase64String( Encoding.UTF8.GetBytes( pat ) );
            return TemporarySetPushTargetAndPasswordLogin( pushUri, pwd );
        }

        public IDisposable TemporarySetPushTargetAndPasswordLogin( string pushUri, string password )
        {
            return NPMRCTokenInjector.PasswordLogin( pushUri, password, NPMRCPath );
        }

        class NPMRCTokenInjector : IDisposable
        {
            static IEnumerable<string> CommentEverything( IEnumerable<string> lines )
            {
                return lines.Select( s => "#" + s );
            }

            static IEnumerable<string> UncommentAndRemoveNotCommented( IEnumerable<string> lines )
            {
                return lines.Where( s => s.StartsWith( "#" ) ).Select( s => s.Substring( 1 ) );
            }

            readonly NormalizedPath _npmrcPath;

            NPMRCTokenInjector( NormalizedPath path )
            {
                _npmrcPath = path;
            }

            static List<string> ReadCommentedLines( NormalizedPath npmrcPath )
            {
                string[] npmrc = File.Exists( npmrcPath ) ? File.ReadAllLines( npmrcPath ) : Array.Empty<string>();
                return CommentEverything( npmrc ).ToList();
            }

            public static NPMRCTokenInjector TokenLogin( string pushUri, string token, NormalizedPath npmrcPath )
            {
                List<string> npmrc = ReadCommentedLines( npmrcPath );
                npmrc.Add( "registry=" + pushUri );
                npmrc.Add( "always-auth=true" );
                npmrc.Add( pushUri.Replace( "https:", "" ) + ":_authToken=" + token );
                File.WriteAllLines( npmrcPath, npmrc );
                return new NPMRCTokenInjector( npmrcPath );
            }

            public static NPMRCTokenInjector PasswordLogin( string pushUri, string password, NormalizedPath npmrcPath )
            {
                List<string> npmrc = ReadCommentedLines( npmrcPath );
                var argPushUri = pushUri.Replace( "https:", "" );
                npmrc.Add( "registry=" + pushUri );
                npmrc.Add( "always-auth=true" );
                npmrc.Add( argPushUri + ":username=CodeCakeBuilder" );
                npmrc.Add( argPushUri + ":_password=" + password );
                npmrc.Add( argPushUri + ":always-auth=true" );
                File.WriteAllLines( npmrcPath, npmrc );
                return new NPMRCTokenInjector( npmrcPath );
            }

            public void Dispose()
            {
                File.WriteAllLines(
                    _npmrcPath,
                    UncommentAndRemoveNotCommented( File.ReadAllLines( _npmrcPath ).ToList() )
                );
            }
        }

    }
}
