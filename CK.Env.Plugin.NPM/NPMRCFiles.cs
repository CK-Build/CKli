using CK.Core;
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
    public class NPMRCFiles : GitBranchPluginBase, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;
        readonly NPMProjectsDriver _driver;
        readonly ISecretKeyStore _secretStore;

        public NPMRCFiles( GitFolder f, NPMProjectsDriver driver, ISecretKeyStore secretStore, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath )
        {
            _solutionSpec = solutionSpec;
            _driver = driver;
            _secretStore = secretStore;
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

        void ApplySettings( IActivityMonitor m, NormalizedPath f )
        {
            var text = GitFolder.FileSystem.GetFileInfo( f ).AsTextFileInfo( ignoreExtension: true )?.TextContent ?? String.Empty;
            var lines = _rLine.Matches( text )
                            .Cast<Match>()
                            .Select( l => l.Groups[2].Length > 0
                                            ? new Line( l.Groups[1].Value, l.Groups[2].Value )
                                            : new Line( l.Value ) )
                            .ToList();


            lines.RemoveAll( p => p.FullKey == "scope" );//remove all keyvalues scopes.

            foreach( var s in _solutionSpec.NPMSources )
            {
                EnsureLine( lines, s.Scope, "registry", s.Url );

                // Scope doesn't carry auth info:
                lines.RemoveAll( line => line.FullKey == s.Scope + ":username" );
                lines.RemoveAll( line => line.FullKey == s.Scope + ":always-auth" );
                lines.RemoveAll( line => line.FullKey == s.Scope + ":_password" );

                // Auth is carried by registry url (from which 'https:' prefix is removed).
                if( !s.Url.StartsWith( "https://" ) ) throw new Exception( $"NPM registry url must start with 'https://': {s.Url}" );
                var scopeUrl = s.Url.Substring( "https:".Length );
                if( s.Credentials != null )
                {
                    EnsureLine( lines, scopeUrl, "username", s.Credentials.UserName );
                    EnsureLine( lines, scopeUrl, "always-auth", "true" );
                    string password = s.Credentials.IsSecretKeyName
                                        ? _secretStore.GetSecretKey( m, s.Credentials.PasswordOrSecretKeyName, throwOnEmpty: true )
                                        : s.Credentials.PasswordOrSecretKeyName;
                    if( s.Url.IndexOf( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) >= 0 )
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
    }
}
