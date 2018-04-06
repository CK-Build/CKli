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
        private readonly XGitCredentials _gitCredentials;

        public XGitFolder(
            Initializer initializer,
            XPathItem parent,
            IssueCollector issueCollector,
            XGitCredentials gitCredentials = null
            )
            : base( initializer, parent.FileSystem, parent )
        {
            initializer.ChildServices.Add( this );
            GitFolder = FileSystem.EnsureGitFolder( FullPath );
            _issueCollector = issueCollector;
            _gitCredentials = gitCredentials;
        }

        /// <summary>
        /// Gets the <see cref="GitFolder"/> object that encapsulates the Git repoistory.
        /// </summary>
        public GitFolder GitFolder { get; private set; }

        /// <summary>
        /// Gets the rul of the remote repository.
        /// </summary>
        public string Url { get; private set; }

        public CredentialsHandler ObtainGitCredentialsProvider( IActivityMonitor m ) => _gitCredentials?.ObtainGitCredentialsHandler( m );

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
                     return EnsureOrCloneGitDirectory( m );
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

        internal bool EnsureOrCloneGitDirectory( IActivityMonitor m )
        {
            if( GitFolder == null )
            {
                m.Info( "Git directory does not exist." );
                if( Url == null )
                {
                    m.Warn( "Url repository is not specified. Skipping Automatic clone." );
                    return false;
                }
                else
                {
                    using( m.OpenInfo( $"Checking out {FullPath} from {Url}" ) )
                    {
                        Repository.Clone( Url, FileInfo.PhysicalPath, new CloneOptions()
                        {
                            CredentialsProvider = ObtainGitCredentialsProvider( m ),
                        } );
                        GitFolder = FileSystem.EnsureGitFolder( FullPath );
                    }
                }
            }
            return true;
        }
    }
}
