using Cake.Common.Diagnostics;
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

            public PackageVersionReplacer(
                NPMProject p,
                SVersion version,
                bool preparePack,
                Action<JObject> packageJsonPreProcessor )
            {
                _p = p;
                _savedPackageJson = File.ReadAllText( p.PackageJson.JsonFilePath );

                JObject json = JObject.Parse( _savedPackageJson );
                json["version"] = version.ToNuGetPackageString();
                if( preparePack )
                {
                    json.Remove( "devDependencies" );
                    json.Remove( "scripts" );
                }
                if( packageJsonPreProcessor != null ) packageJsonPreProcessor( json );
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
                JObject json = JObject.Parse( File.ReadAllText( JsonFilePath ) );
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

        /// <summary>
        /// Gets whether this package has a named script entry.
        /// </summary>
        /// <param name="name">Script name.</param>
        /// <returns>Whether the script exists.</returns>
        public bool HasScript( string name ) => PackageJson.Scripts.Contains( name );

        /// <summary>
        /// Finds either "baseName-debug", "baseName-release" depending on <paramref name="isRelease"/>
        /// and falls back to baseName if specific scripts don't exist.
        /// By default, at least 'baseName' must exist otherwise an InvalidOperationException is thrown.
        /// </summary>
        /// <param name="isRelease">True for release, false for debug.</param>
        /// <param name="baseName">Base name to look for.</param>
        /// <param name="checkBaseNameExist">
        /// By default, at least baseName must exist otherwise an InvalidOperationException is thrown.
        /// When false, no check is done for baseName that is returned as-is.
        /// When null (and baseName cannot be found), null is returned.
        /// </param>
        /// <returns>The best script (or null if it doesn't exist and <paramref name="checkBaseNameExist"/> is null).</returns>
        public string FindBestScript( bool isRelease, string baseName, bool? checkBaseNameExist = true )
        {
            string n;
            if( (isRelease && HasScript( (n = baseName + "-release") )) || (!isRelease && HasScript( (n = baseName + "-debug") )) )
            {
                return n;
            }
            if( checkBaseNameExist == null )
            {
                return HasScript( baseName ) ? baseName : null;
            }
            else if( checkBaseNameExist == true )
            {
                if( HasScript( baseName ) ) return baseName;
                throw new InvalidOperationException( $"Missing script '{baseName}' in {PackageJson.JsonFilePath}." );
            }
            return baseName;
        }

        /// <summary>
        /// Finds either "name-debug", "name-release" depending on <see cref="StandardGlobalInfo.IsRelease"/>
        /// and falls back to "name". By default if no script is found an <see cref="InvalidOperationException"/>
        /// is thrown. To emit a Cake warning (and return null) if the script can't be found,
        /// let <paramref name="scriptMustExist"/> be false.
        /// </summary>
        /// <param name="globalInfo">The global info object.</param>
        /// <param name="name">Base script name to look for.</param>
        /// <param name="scriptMustExist">
        /// False to emit a warning and return null if the script doesn't exist.
        /// By default if no script is found an <see cref="InvalidOperationException"/> is thrown.
        /// </param>
        /// <returns>The best script (or null if it doesn't exist and <paramref name="scriptMustExist"/> is false).</returns>
        public string FindBestScript( StandardGlobalInfo globalInfo, string name, bool scriptMustExist = true )
        {
            string n = FindBestScript( globalInfo.IsRelease, name, scriptMustExist ? (bool?)true : null );
            if( n == null )
            {
                globalInfo.Cake.Warning( $"Missing script '{name}' in '{PackageJson.JsonFilePath}'." );
            }
            return n;
        }

        /// <summary>
        /// Runs a "npm install" followed by a call to the clean script (that must exist, see <see cref="FindBestScript(StandardGlobalInfo, string, bool)"/>). 
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        /// <param name="cleanScriptName">Clean script name.</param>
        /// <returns>False if the script doesn't exist (<paramref name="scriptMustExist"/> is false), otherwise true.</returns>
        public virtual void RunInstallAndClean( StandardGlobalInfo globalInfo, bool scriptMustExist = true, string cleanScriptName = "clean" )
        {
            RunInstall( globalInfo );
            RunScript( globalInfo, cleanScriptName, scriptMustExist );
        }

        /// <summary>
        /// Runs the 'name-debug', 'name-release' or 'name' script (see <see cref="FindBestScript(StandardGlobalInfo, string, bool)"/>).
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        /// <returns>False if the script doesn't exist (<paramref name="scriptMustExist"/> is false), otherwise true.</returns>
        public bool RunScript( StandardGlobalInfo globalInfo, string name, bool scriptMustExist = true )
        {
            string n = FindBestScript( globalInfo, name, scriptMustExist );
            if( n == null ) return false;
            DoRunScript( globalInfo, n );
            return true;
        }

        private protected virtual void DoRunScript( StandardGlobalInfo globalInfo, string n )
        {
            globalInfo.Cake.NpmRunScript(
                    n,
                    s => s
                        .WithLogLevel( NpmLogLevel.Info )
                        .FromPath( DirectoryPath.Path )
                );
        }

        /// <summary>
        /// Runs "build" script: see <see cref="RunScript(StandardGlobalInfo, string, bool)"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        /// <returns>False if the script doesn't exist (<paramref name="scriptMustExist"/> is false), otherwise true.</returns>
        public bool RunBuild( StandardGlobalInfo globalInfo, bool scriptMustExist = true ) => RunScript( globalInfo, "build", scriptMustExist );

        /// <summary>
        /// Runs "test" script: see <see cref="RunScript(StandardGlobalInfo, string, bool)"/>.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="scriptMustExist">
        /// False to only emit a warning and return false if the script doesn't exist instead of
        /// throwing an exception.
        /// </param>
        /// <returns>False if the script doesn't exist (<paramref name="scriptMustExist"/> is false), otherwise true.</returns>
        public void RunTest( StandardGlobalInfo globalInfo, bool scriptMustExist = true ) => RunScript( globalInfo, "test", scriptMustExist );


        public IDisposable TemporarySetVersion( SVersion version ) => new PackageVersionReplacer( this, version, false, null );

        private protected IDisposable TemporaryPrePack( SVersion version, bool cleanupPackageJson, Action<JObject> packageJsonPreProcessor )
        {
            return new PackageVersionReplacer( this, version, cleanupPackageJson, packageJsonPreProcessor );
        }

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
