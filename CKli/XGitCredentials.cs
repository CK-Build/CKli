using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using System;

namespace CKli
{
    public class XGitCredentials : XTypedObject
    {
        public XGitCredentials( Initializer initializer )
            : base( initializer )
        {
            // Add to DI
            initializer.Services.Add( this );
        }

        public string UsernameEnvironmentVariable { get; set; }

        public string PasswordEnvironmentVariable { get; set; }

        internal string GetUsername( IActivityMonitor m )
        {
            if( string.IsNullOrEmpty( UsernameEnvironmentVariable ) )
            {
                m.Warn( "No environment variable name is set for Git username." );
                return null;
            }
            else
            {
                m.Debug( $"Using Git username from environment variable '{UsernameEnvironmentVariable}'" );
                return Environment.GetEnvironmentVariable( UsernameEnvironmentVariable );
            }
        }

        internal string GetPassword( IActivityMonitor m )
        {
            if( string.IsNullOrEmpty( PasswordEnvironmentVariable ) )
            {
                m.Warn( "No environment variable name is set for Git password." );
                return null;
            }
            else
            {
                m.Debug( $"Using Git password from environment variable '{PasswordEnvironmentVariable}'" );
                return Environment.GetEnvironmentVariable( PasswordEnvironmentVariable );
            }
        }

        internal (string username, string password) GetUsernamePassword( IActivityMonitor m )
        {
            return (GetUsername( m ), GetPassword( m ));
        }

        internal CredentialsHandler ObtainGitCredentialsHandler( IActivityMonitor m )
        {
            return ( url, usernameFromUrl, types ) =>
            {
                Credentials creds = null;
                if( types.HasFlag( SupportedCredentialTypes.UsernamePassword ) )
                {
                    (string username, string password) = GetUsernamePassword( m );

                    if( string.IsNullOrEmpty( usernameFromUrl ) )
                    {
                        if( string.IsNullOrEmpty( username ) )
                        {
                            m.Warn( $"Git username was not found in environment variable or URL. Git may fail if the repository requires authentication." );
                        }
                        else
                        {
                            m.Info( $"Using Git username: '{username}' (from environment variable)" );
                        }
                    }
                    else
                    {
                        m.Info( $"Using Git username: '{username}' (from URL: '{url}')" );
                    }

                    // Read password from env. var

                    if( !string.IsNullOrEmpty( username ) )
                    {
                        if( string.IsNullOrEmpty( password ) )
                        {
                            m.Warn( $"Git password was not found in environment variable. Git might fail if the repository requires authentication." );
                        }
                        else
                        {
                            m.Info( $"Using Git password (from environment variable)" );
                            creds = new UsernamePasswordCredentials() { Username = username, Password = password };
                        }
                    }

                }

                // Fallback to Default if possible
                if( creds == null && types.HasFlag( SupportedCredentialTypes.Default ) )
                {
                    m.Info( $"Using default credentials." );
                    creds = new DefaultCredentials();
                }

                if( creds == null )
                {
                    m.Warn( $"No supported authentication credentials can be used. Supported types: {types.ToString()}" );
                }

                return creds;
            };
        }
    }
}
