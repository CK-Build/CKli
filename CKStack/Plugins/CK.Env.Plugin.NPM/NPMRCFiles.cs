using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NodeSln;
using CK.Env.NPM;
using CK.SimpleKeyVault;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env.Plugin
{
    public class NPMRCFiles : GitBranchPluginBase, IDisposable, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;
        readonly NodeSolutionDriver _driver;
        readonly SecretKeyStore _secretStore;

        public NPMRCFiles( GitRepository f, NodeSolutionDriver nodeDriver, SecretKeyStore secretStore, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath )
        {
            _solutionSpec = solutionSpec;
            _secretStore = secretStore;
            _driver = nodeDriver;
            nodeDriver.SolutionDriver.OnSolutionConfiguration += OnSolutionConfiguration;
        }


        NormalizedPath ICommandMethodsProvider.CommandProviderName => _driver.BranchPath.AppendPart( nameof( NPMRCFiles ) );

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m )
                || !_driver.TryGetSolution( m, out var solution, out var nodeSolution ) ) return;

            foreach( var project in nodeSolution.RootProjects )
            {
                var npmrcPath = project.Path.AppendPart( ".npmrc" );
                if( project.UseYarn )
                {
                    GitFolder.FileSystem.Delete( m, npmrcPath );
                }
                else
                {
                    DoApplySettings( m, solution, npmrcPath );
                }
            }
        }

        static readonly Regex _rLine = new Regex( "^(?<1>(;|#).*)\\r$|^(?<1>.+?]*)\\s*=\\s*(?<2>.+?)\\r?$",
                                                    RegexOptions.Multiline
                                                    | RegexOptions.ExplicitCapture
                                                    | RegexOptions.CultureInvariant
                                                    | RegexOptions.Compiled );

        readonly struct Line
        {
            public readonly string? Scope;
            public readonly string FullKey;
            public readonly string? Value;

            public string? VoidLine => Value == null ? FullKey : null;

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
            // the .npmrc file: once done, restore can be made and having these keys available
            // as environment variables will not help.
            var creds = e.Solution.ArtifactSources.OfType<INPMFeed>()
                            .Where( s => s.Credentials != null && s.Credentials.IsSecretKeyName )
                            .Select( s => s.Credentials.PasswordOrSecretKeyName );
            foreach( var c in creds )
            {
                Debug.Assert( c != null );
                _secretStore.DeclareSecretKey( c!, current => current?.Description == null
                                                                ? "Needed to configure .npmrc file."
                                                                : current.Description + Environment.NewLine + "Needed to configure .npmrc file." );
            }
        }

        void DoApplySettings( IActivityMonitor m, ISolution solution, NormalizedPath npmrcPath )
        {
            string text = GitFolder.FileSystem.GetFileInfo( npmrcPath )
                                              .AsTextFileInfo( ignoreExtension: true )?.TextContent
                          ?? String.Empty;

            List<Line> lines = _rLine.Matches( text )
                                      .Cast<Match>()
                                      .Select( l => l.Groups[2].Length > 0
                                                      ? new Line( l.Groups[1].Value, l.Groups[2].Value )
                                                      : new Line( l.Value ) )
                                      .ToList();

            // Remove all key values scopes.
            lines.RemoveAll( p => p.FullKey == "scope" );

            foreach( var feed in solution.ArtifactSources.OfType<INPMFeed>() )
            {
                if( feed.Url.StartsWith( "file:" ) )
                {
                    m.Info( "Npm does not support file repository. Skipping." );
                    continue;
                }
                EnsureLine( lines, feed.Scope, "registry", feed.Url );

                // Scope doesn't carry auth info:
                lines.RemoveAll( line => line.FullKey == feed.Scope + ":username" );
                lines.RemoveAll( line => line.FullKey == feed.Scope + ":always-auth" );
                lines.RemoveAll( line => line.FullKey == feed.Scope + ":_password" );
                Uri uri = new Uri( feed.Url );
                if( uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps ) throw new Exception( $"NPM registry url must start with 'https://': {feed.Url}" );
                // Auth is carried by registry url (from which 'http(s):' prefix is removed).
                var scopeUrl = feed.Url.Substring( "https:".Length );
                if( feed.Credentials != null )
                {
                    EnsureLine( lines, scopeUrl, "username", feed.Credentials.UserName );
                    EnsureLine( lines, scopeUrl, "always-auth", "true" );
                    string? password = feed.Credentials.IsSecretKeyName
                                        ? _secretStore.GetSecretKey( m, feed.Credentials.PasswordOrSecretKeyName, false )
                                        : feed.Credentials.PasswordOrSecretKeyName;
                    if( password == null )
                    {
                        if( feed.Credentials.IsSecretKeyName )
                            m.Warn( $"Secret '{feed.Credentials.PasswordOrSecretKeyName}' is not known. Configuration for feed '{feed.Name}' skipped." );
                        else m.Warn( $"Empty feed password. Configuration for feed '{feed.Name}' skipped." );
                        continue;
                    }
                    if( feed.Url.IndexOf( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) >= 0 )
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
            GitFolder.FileSystem.CopyTo( m, lines.Select( l => l.ToString() ).Concatenate( "\r\n" ), npmrcPath );
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
            _driver.SolutionDriver.OnSolutionConfiguration -= OnSolutionConfiguration;
        }
    }
}
