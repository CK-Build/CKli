using CK.Core;
using CK.Env.NPM;
using CK.SimpleKeyVault;
using Microsoft.IO;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CK.Env.Plugin
{
    public class NPMRCFiles : GitBranchPluginBase, IDisposable, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;
        readonly NPMProjectsDriver _driver;
        readonly SolutionDriver _solutionDriver;
        readonly SecretKeyStore _secretStore;

        public NPMRCFiles( GitRepository f, NPMProjectsDriver driver, SolutionDriver solutionDriver, SecretKeyStore secretStore, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath )
        {
            _solutionDriver = solutionDriver;
            _solutionSpec = solutionSpec;
            _driver = driver;
            _secretStore = secretStore;
            _solutionDriver.OnSolutionConfiguration += OnSolutionConfiguration;
        }


        NormalizedPath ICommandMethodsProvider.CommandProviderName => _driver.BranchPath.AppendPart( nameof( NPMRCFiles ) );

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;

            var projects = _driver.GetSimpleNPMProjects( m );
            if( projects == null ) return;

            foreach( var project in projects )
            {

                var npmrcPath = project.FullPath.AppendPart( ".npmrc" );
                var yarnRcPath = project.FullPath.AppendPart( ".yarnrc.yml" );
                (bool error, bool isUsingYarn) = IsUsingYarn( m, project, GitFolder );
                if( error ) continue;
                if( !isUsingYarn )
                {
                    DoApplySettings( m, npmrcPath, yarnRcPath );
                }
            }
        }

        public static (bool error, bool isUsingYarn) IsUsingYarn( IActivityMonitor m, NPMProject project, GitRepository gitFolder )
        {
            using( var grp = m.OpenInfo( $"Is the project {project.Project.Name} using yarn ?" ) )
            {

                var npmLockPath = project.FullPath.AppendPart( "package-lock.json" );
                var npmLockFileInfo = gitFolder.FileSystem.GetFileInfo( npmLockPath );
                var yarnLockPath = project.FullPath.AppendPart( "yarn.lock" );
                var yarnLockFileInfo = gitFolder.FileSystem.GetFileInfo( yarnLockPath );
                if( npmLockFileInfo.Exists && yarnLockFileInfo.Exists )
                {
                    m.Error( $"Both {npmLockPath.LastPart} and {yarnLockPath.LastPart} exists. Using 2 packages manager at the same times is not supported." );
                    return (true, false);
                }
                if( !npmLockFileInfo.Exists && !yarnLockFileInfo.Exists )
                {
                    m.Error( $"Did not found a {npmLockPath.LastPart} or {yarnLockPath.LastPart}, cannot determine used package manage." );
                    return (true, false);
                }
                bool useYarn = yarnLockFileInfo.Exists;
                m.CloseGroup( useYarn );
                return (false, useYarn);
            }
        }

        static readonly Regex _rLine = new Regex( "^(?<1>(;|#).*)\\r$|^(?<1>.+?]*)\\s*=\\s*(?<2>.+?)\\r?$",
                                                    RegexOptions.Multiline
                                                    | RegexOptions.ExplicitCapture
                                                    | RegexOptions.CultureInvariant
                                                    | RegexOptions.Compiled );

        readonly struct Line
        {
            public readonly string Scope;
            public readonly string FullKey;
            public readonly string Value;

            public string VoidLine => Value == null ? FullKey : null;

            public Line( string fullKey, string value )
            {
                Debug.Assert( fullKey != null && value != null );
                int idx = fullKey.IndexOf( ':' );
                Scope = idx >= 0
                            ? fullKey.Substring( 0, idx )
                            : null;
                FullKey = fullKey;
                Value = value;
            }
            public Line( string commentLine )
            {
                Debug.Assert( commentLine != null
                                && commentLine.Length > 0
                                && (commentLine[0] == ';' || commentLine[1] == '#') );
                Scope = null;
                FullKey = commentLine;
                Value = null;
            }

            public override string ToString() => VoidLine ?? $"{FullKey} = {Value}";

        }


        void OnSolutionConfiguration( object? sender, SolutionConfigurationEventArgs e )
        {
            // These values are not build secrets. They are required by ApplySettings to configure
            // the NuGet.config file: once done, restore can be made and having these keys available
            // as environment variables will not help.
            var creds = e.Solution.ArtifactSources.OfType<INPMFeed>()
                            .Where( s => s.Credentials != null && s.Credentials.IsSecretKeyName )
                            .Select( s => s.Credentials.PasswordOrSecretKeyName );
            foreach( var c in creds )
            {
                _secretStore.DeclareSecretKey( c, current => current?.Description ?? "Needed to configure .npmrc file." );
            }
        }

        void DoApplySettings( IActivityMonitor m, NormalizedPath npmrc, NormalizedPath yarnRc )
        {
            var s = _solutionDriver.GetSolution( m, allowInvalidSolution: true );
            if( s == null ) return;

            GitFolder.FileSystem.Delete( m, yarnRc );

            string text = GitFolder.FileSystem.GetFileInfo( npmrc ).AsTextFileInfo( ignoreExtension: true )?.TextContent ?? String.Empty;
            List<Line> lines = _rLine.Matches( text )
                            .Cast<Match>()
                            .Select( l => l.Groups[2].Length > 0
                                            ? new Line( l.Groups[1].Value, l.Groups[2].Value )
                                            : new Line( l.Value ) )
                            .ToList();


            lines.RemoveAll( p => p.FullKey == "scope" );//remove all keyvalues scopes.

            foreach( var p in s.ArtifactSources.OfType<INPMFeed>() )
            {

                if( p.Url.StartsWith( "file:" ) )
                {
                    m.Info( "Npm does not support file repository. Skipping." );
                    continue;
                }
                EnsureLine( lines, p.Scope, "registry", p.Url );

                // Scope doesn't carry auth info:
                lines.RemoveAll( line => line.FullKey == p.Scope + ":username" );
                lines.RemoveAll( line => line.FullKey == p.Scope + ":always-auth" );
                lines.RemoveAll( line => line.FullKey == p.Scope + ":_password" );
                Uri uri = new Uri( p.Url );
                if( uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps ) throw new Exception( $"NPM registry url must start with 'https://': {p.Url}" );
                // Auth is carried by registry url (from which 'http(s):' prefix is removed).
                var scopeUrl = p.Url.Substring( "https:".Length );
                if( p.Credentials != null )
                {
                    EnsureLine( lines, scopeUrl, "username", p.Credentials.UserName );
                    EnsureLine( lines, scopeUrl, "always-auth", "true" );
                    string password = p.Credentials.IsSecretKeyName
                                        ? _secretStore.GetSecretKey( m, p.Credentials.PasswordOrSecretKeyName, false )
                                        : p.Credentials.PasswordOrSecretKeyName;
                    if( password == null )
                    {
                        if( p.Credentials.IsSecretKeyName )
                            m.Warn( $"Secret '{p.Credentials.PasswordOrSecretKeyName}' is not known. Configuration for feed '{s.Name}' skipped." );
                        else m.Warn( $"Empty feed password. Configuration for feed '{s.Name}' skipped." );
                        continue;
                    }
                    if( p.Url.IndexOf( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) >= 0 )
                    {
                        password = Convert.ToBase64String( Encoding.UTF8.GetBytes( password ) );
                    }
                    EnsureLine( lines, scopeUrl, "_password", password );
                }
                else
                {
                    // Cleanup any auth info.
                    lines.RemoveAll( line => line.FullKey == scopeUrl + ":username" );
                    lines.RemoveAll( line => line.FullKey == scopeUrl + ":always-auth" );
                    lines.RemoveAll( line => line.FullKey == scopeUrl + ":_password" );
                }
            };
            EnsureLine( lines, "git-tag-version", "false" );
            lines.RemoveAll( line => line.Scope != null && _solutionSpec.RemoveNPMScopeNames.Contains( line.Scope ) );
            GitFolder.FileSystem.CopyTo( m, lines.Select( l => l.ToString() ).Concatenate( "\r\n" ), npmrc );
        }

        void EnsureLine( IList<Line> lines, string scope, string key, string value )
        {
            EnsureLine( lines, scope + ':' + key, value );
        }

        void EnsureLine( IList<Line> lines, string key, string value )
        {
            int idx = lines.IndexOf( line => line.FullKey == key );
            if( idx < 0 ) lines.Add( new Line( key, value ) );
            else lines[idx] = new Line( key, value );
        }

        public void Dispose()
        {
            _solutionDriver.OnSolutionConfiguration -= OnSolutionConfiguration;
        }
    }
}
