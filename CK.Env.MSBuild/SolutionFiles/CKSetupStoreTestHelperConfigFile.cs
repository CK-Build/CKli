using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class CKSetupStoreTestHelperConfigFile : GitFolderTextFileBase, IGitBranchPlugin, IDisposable
    {
        readonly ISolutionSettings _settings;
        readonly ILocalFeedProvider _localFeedProvider;

        public CKSetupStoreTestHelperConfigFile(
            GitFolder f,
            ISolutionSettings settings,
            ILocalFeedProvider localFeedProvider,
            NormalizedPath branchPath )
            : base( f, branchPath.AppendPart( "RemoteStore.TestHelper.config" ) )
        {
            _settings = settings;
            _localFeedProvider = localFeedProvider;
            BranchPath = branchPath;
            if( IsActive )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
        }

        public NormalizedPath BranchPath { get; }

        public bool IsActive => Folder.StandardGitStatus == StandardGitStatus.LocalBranch && _settings.ProduceCKSetupComponents;

        public void Dispose()
        {
            if( IsActive )
            {
                Folder.OnLocalBranchEntered -= OnLocalBranchEntered;
                Folder.OnLocalBranchLeaving -= OnLocalBranchLeaving;
            }
        }

        void OnLocalBranchLeaving( object sender, EventMonitoredArgs e )
        {
            Delete( e.Monitor );
        }

        void OnLocalBranchEntered( object sender, EventMonitoredArgs e )
        {
            var storePath = _localFeedProvider.GetLocalCKSetupStorePath( e.Monitor );
            EnsureStorePath( e.Monitor, storePath );
        }

        public bool EnsureStorePath( IActivityMonitor m, string storePath )
        {
            var text = "<configuration><appSettings>" + Environment.NewLine;
            text += "  -- This forces the Solutions that generate components to use the LocalFeed/Local/CKSetupStore" + Environment.NewLine; ;
            text += $@"  <add key=""CKSetup/DefaultStoreUrl"" value=""{storePath}"" />" + Environment.NewLine;
            text += $@"  <add key=""CKSetup/DefaultStorePath"" value=""{storePath}"" />" + Environment.NewLine;
            text += "</appSettings></configuration>";
            return CreateOrUpdate( m, text );
        }
    }
}
