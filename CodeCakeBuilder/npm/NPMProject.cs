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
                _savedPackageJson = File.ReadAllText( p.PackageJsonPath );

                // Replace token by SafeSemVersion
                JObject json = JObject.Parse( _savedPackageJson );
                json["version"] = version.ToNuGetPackageString();
                File.WriteAllText( p.PackageJsonPath, json.ToString() );
            }

            public void Dispose()
            {
                File.WriteAllText( _p.PackageJsonPath.Path, _savedPackageJson );
            }
        }

        public NPMProject( NormalizedPath path )
        {
            DirectoryPath = path;
            PackageJsonPath = path.AppendPart( "package.json" );
            NPMRCPath = path.AppendPart( ".npmrc" );
        }

        public virtual bool IsPublished => false;

        public NormalizedPath DirectoryPath { get; }

        public NormalizedPath PackageJsonPath { get; }

        public NormalizedPath NPMRCPath { get; }

        public virtual void RunBuild( StandardGlobalInfo globalInfo )
        {
            globalInfo.Cake.NpmRunScript(
                    "build",
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
