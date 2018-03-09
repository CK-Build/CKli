using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using System;

namespace CKli
{
    public class XGitFolder : XPathItem
    {
        private readonly IssueCollector _issueCollector;

        public XGitFolder(
            Initializer initializer,
            XPathItem parent,
            IssueCollector issueCollector
            )
            : base( initializer, parent.FileSystem, parent )
        {
            initializer.ChildServices.Add( this );
            GitFolder = FileSystem.EnsureGitFolder( FullPath );
            _issueCollector = issueCollector;
        }

        public GitFolder GitFolder { get; private set; }

        public string Url { get; private set; }

        protected override bool DoRun( IRunContext ctx )
        {
            if( FileInfo.Exists )
            {
                if( FileInfo.IsDirectory )
                {
                    if( GitFolder == null )
                    {
                        ctx.Monitor.Fatal( $"Git directory at {FullPath} not a Git working directory." );
                        return false;
                    }
                    return base.DoRun( ctx );
                }
                else
                {
                    ctx.Monitor.Fatal( $"Git directory at {FullPath} is not a directory. It must be a directory." );
                    return false;
                }
            }
            // No directory: Checkout
            _issueCollector.RunIssueFactory( ctx.Monitor, ( ib ) =>
             {
                 bool DoFix( IActivityMonitor m )
                 {
                     using( m.OpenInfo( $"Checking out {FullPath} from {Url}" ) )
                     {
                         Repository.Clone( Url, FileInfo.PhysicalPath, new CloneOptions()
                         {
                             CredentialsProvider = XGitFolder.ObtainGitCredentialsHandler( m ),
                         } );
                     }
                     return true;
                 }

                 using( ib.Monitor.OpenInfo( $"Missing Git working directory: {FullPath}" ) )
                 {
                     if( Url == null ) ib.Monitor.Warn( "Url repository is not specified. Skipping Autmatic clone." );

                     ib.CreateIssue(
                         $"MissingGitFolder:{FullPath}",
                         "Git folder does not exist",
                         Url != null ? DoFix : (Func<IActivityMonitor, bool>)null
                         );
                     return true;
                 }
             } );

            return true;
        }

        public static CredentialsHandler ObtainGitCredentialsHandler( IActivityMonitor m )
        {
            return ( url, usernameFromUrl, types ) =>
            {
                string domain = new Uri( url ).Host;
                string usernameEnvironmentVariableKey = $"GIT_USER_{domain}";
                string passwordEnvironmentVariableKey = $"GIT_PWD_{domain}";

                m.Info( $"Looking for credentials for domain {domain} in environment: {usernameEnvironmentVariableKey}, {passwordEnvironmentVariableKey}, and URL" );

                // Guess username: use URL, fallback on env. var
                string username = usernameFromUrl;
                if( string.IsNullOrEmpty( username ) )
                {
                    username = Environment.GetEnvironmentVariable( usernameEnvironmentVariableKey );

                    if( string.IsNullOrEmpty( username ) )
                    {
                        m.Warn( $"Git username was not found at environment variable {usernameEnvironmentVariableKey}. Git might fail if the repository requires authentication." );
                    }
                    else
                    {
                        m.Info( $"Using Git username from environment variable ({usernameEnvironmentVariableKey}): {username}" );
                    }
                }
                else
                {
                    m.Info( $"Using Git username from URL: {username}" );
                }

                // Read password from env. var

                if( !string.IsNullOrEmpty( username ) )
                {
                    string password = Environment.GetEnvironmentVariable( passwordEnvironmentVariableKey );

                    if( string.IsNullOrEmpty( password ) )
                    {
                        m.Warn( $"Git password was not found at environment variable {passwordEnvironmentVariableKey}. Git might fail if the repository requires authentication." );
                    }
                    else
                    {
                        m.Info( $"Using Git password from environment variable ({passwordEnvironmentVariableKey})" );
                        return new UsernamePasswordCredentials() { Username = username, Password = password };
                    }
                }
                return new DefaultCredentials();
            };
        }
    }
}
