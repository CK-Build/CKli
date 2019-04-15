using System;
using System.Collections.Generic;
using Cake.Npm;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.IO;
using CSemVer;
using Cake.Npm.Pack;
using CodeCake.Abstractions;
using System.Text;

namespace CodeCake
{
    public partial class Build
    {
        public class NpmrcTokenInjector : IDisposable
        {
            static List<string> CommentEverything( List<string> lines )
            {
                return lines.Select( s => "#" + s ).ToList();
            }
            static List<string> UncommentAndRemoveNotCommented( List<string> lines )
            {
                return lines.Where( s => s.StartsWith( "#" ) ).Select( s => s.Substring( 1 ) ).ToList();
            }

            readonly string _npmrcPath;

            NpmrcTokenInjector( string path )
            {
                _npmrcPath = path;
            }

            public static NpmrcTokenInjector TokenLogin( string pushUri, string token, string npmrcPath )
            {
                if( !File.Exists( npmrcPath ) ) throw new ArgumentException( "npmrcPath File does not exist" );
                List<string> npmrc = File.ReadAllLines( npmrcPath ).ToList();
                npmrc = CommentEverything( npmrc );
                npmrc.Add( "registry=" + pushUri );
                npmrc.Add( "always-auth=true" );
                npmrc.Add( pushUri.Replace( "https:", "" ) + ":_authToken=" + token );
                File.WriteAllLines( npmrcPath, npmrc.ToArray() );
                return new NpmrcTokenInjector( npmrcPath );
            }

            public static NpmrcTokenInjector VstsPatLogin( string pushUri, string pat, string npmrcPath )
            {
                return PasswordLogin( pushUri, Convert.ToBase64String( Encoding.UTF8.GetBytes( pat ) ), npmrcPath );
            }

            public static NpmrcTokenInjector PasswordLogin( string pushUri, string password, string npmrcPath )
            {
                if( !File.Exists( npmrcPath ) ) throw new ArgumentException( "npmrcPath File does not exist" );
                List<string> npmrc = File.ReadAllLines( npmrcPath ).ToList();
                var argPushUri = pushUri.Replace( "https:", "" );
                npmrc = CommentEverything( npmrc );
                npmrc.Add( "registry=" + pushUri );
                npmrc.Add( "always-auth=true" );
                npmrc.Add( argPushUri + ":username=CodeCakeBuilder" );
                npmrc.Add( argPushUri + ":_password=" + password );
                npmrc.Add( argPushUri + ":always-auth=true" );
                File.WriteAllLines( npmrcPath, npmrc.ToArray() );
                return new NpmrcTokenInjector( npmrcPath );
            }

            public void Dispose()
            {
                File.WriteAllLines(
                    _npmrcPath,
                    UncommentAndRemoveNotCommented( File.ReadAllLines( _npmrcPath ).ToList() ).ToArray()
                );
            }
        }
        public class PackageVersionReplacer : IDisposable
        {
            readonly (string originalPath, TemporaryFile tempFile) _originalToCopyPaths;

            public PackageVersionReplacer( SVersion version, string packageJsonPath )
            {
                if( version == null || !version.IsValid )
                {
                    throw new ArgumentNullException( nameof( version ) );
                }
                if( packageJsonPath == null ) throw new ArgumentNullException();

                // Copy file to temp. file
                TemporaryFile tf = new TemporaryFile( true, Path.GetExtension( packageJsonPath ).Trim( '.' ) );
                _originalToCopyPaths = (packageJsonPath, tf);
                File.Copy( packageJsonPath, tf.Path, true );

                // Replace token by SafeSemVersion
                JObject json = JObject.Parse( File.ReadAllText( packageJsonPath ) );
                json["version"] = version.ToString();
                File.WriteAllText( packageJsonPath, json.ToString() );
            }
            public void Dispose()
            {
                File.Copy( _originalToCopyPaths.tempFile.Path, _originalToCopyPaths.originalPath, true );
                _originalToCopyPaths.tempFile.Dispose();
            }
        }

        public void NpmPackWithNewVersion( SVersion version, string projectPath, string releasesDir )
        {
            PackageJson packageJson;
            using( var versionReplacer = new PackageVersionReplacer( version, Path.Combine( projectPath, "package.json" ) ) )
            {
                Cake.NpmPack( new NpmPackSettings()
                {
                    WorkingDirectory = projectPath,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                } );
                packageJson = PackageJson.FromDirectoryPath( projectPath );
            }//The old version is restored
            string packName = GetTgzNameOfPackage( packageJson.ArtifactInstance );
            string packPath = Path.Combine( projectPath, packName );
            File.Delete( Path.Combine( releasesDir, packName ) );
            File.Move( packPath, Path.Combine( releasesDir, packName ) );
        }

        public static string GetTgzNameOfPackage( ArtifactInstance packageJson )
        {
            string name = packageJson.Artifact.Name.Replace( "@", "" ).Replace( '/', '-' );
            return name + "-" + packageJson.Version.ToString() + ".tgz";
        }

    }
}
