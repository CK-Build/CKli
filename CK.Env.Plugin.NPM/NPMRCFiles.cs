using CK.Core;
using CK.Env.NPM;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env.Plugin
{
    /// <summary>
    /// 
    /// </summary>
    public class NPMRCFiles : GitBranchPluginBase, IDisposable, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;
        readonly NPMProjectsDriver _driver;
        readonly SolutionDriver _solutionDriver;
        readonly SecretKeyStore _secretStore;

        public NPMRCFiles( GitFolder f, NPMProjectsDriver driver, SolutionDriver solutionDriver, SecretKeyStore secretStore, SolutionSpec solutionSpec, NormalizedPath branchPath )
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

            var projects = _driver.GetNPMProjects( m );
            if( projects == null ) return;
            foreach( var f in projects.Select( p => p.FullPath.AppendPart( ".npmrc" ) ) )
            {
                ApplySettings( m, f );
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


        void OnSolutionConfiguration( object sender, SolutionConfigurationEventArgs e )
        {
            // These values are not build secrets. They are required by ApplySettings to configure
            // the NuGet.config file: once done, restore can be made and having these keys available
            // as environement variables will not help.
            var creds = e.Solution.ArtifactSources.OfType<INPMFeed>()
                            .Where( s => s.Credentials != null && s.Credentials.IsSecretKeyName )
                            .Select( s => s.Credentials.PasswordOrSecretKeyName );
            foreach( var c in creds )
            {
                _secretStore.DeclareSecretKey( c, current => current?.Description ?? "Needed to configure .npmrc file." );
            }
        }
        void ApplySettings( IActivityMonitor m, NormalizedPath f )
        {
            var s = _solutionDriver.GetSolution( m, allowInvalidSolution: true );
            if( s == null ) return;

            var text = GitFolder.FileSystem.GetFileInfo( f ).AsTextFileInfo( ignoreExtension: true )?.TextContent ?? String.Empty;
            var lines = _rLine.Matches( text )
                            .Cast<Match>()
                            .Select( l => l.Groups[2].Length > 0
                                            ? new Line( l.Groups[1].Value, l.Groups[2].Value )
                                            : new Line( l.Value ) )
                            .ToList();


            lines.RemoveAll( p => p.FullKey == "scope" );//remove all keyvalues scopes.

            foreach( var p in s.ArtifactSources.OfType<INPMFeed>() )
            {
                EnsureLine( lines, p.Scope, "registry", p.Url );

                // Scope doesn't carry auth info:
                lines.RemoveAll( line => line.FullKey == p.Scope + ":username" );
                lines.RemoveAll( line => line.FullKey == p.Scope + ":always-auth" );
                lines.RemoveAll( line => line.FullKey == p.Scope + ":_password" );

                // Auth is carried by registry url (from which 'https:' prefix is removed).
                if( !p.Url.StartsWith( "https://" ) ) throw new Exception( $"NPM registry url must start with 'https://': {p.Url}" );
                var scopeUrl = p.Url.Substring( "https:".Length );
                if( p.Credentials != null )
                {
                    EnsureLine( lines, scopeUrl, "username", p.Credentials.UserName );
                    EnsureLine( lines, scopeUrl, "always-auth", "true" );
                    string password = p.Credentials.IsSecretKeyName
                                        ? _secretStore.GetSecretKey( m, p.Credentials.PasswordOrSecretKeyName, throwOnUnavailable: true )
                                        : p.Credentials.PasswordOrSecretKeyName;
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
            GitFolder.FileSystem.CopyTo( m, lines.Select( l => l.ToString() ).Concatenate( "\r\n" ), f );
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
