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
                             CredentialsProvider = GitFolder.ObtainGitCredentialsHandler( m ),
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
    }
}
