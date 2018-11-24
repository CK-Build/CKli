using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CKSetupStoreTestHelperConfigFile : GitFolderTextFileBase, IGitBranchPlugin, ICommandMethodsProvider, IDisposable
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
            if( IsOnLocalBranch )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
        }

        public NormalizedPath BranchPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( IsOnLocalBranch && _settings.ProduceCKSetupComponents )
            {
                EnsureLocalStorePath( m );
            }
            else
            {
                Delete( m );
            }
        }

        public bool IsOnLocalBranch => BranchPath.LastPart == Folder.World.LocalBranchName;

        public void Dispose()
        {
            if( IsOnLocalBranch )
            {
                Folder.OnLocalBranchEntered -= OnLocalBranchEntered;
                Folder.OnLocalBranchLeaving -= OnLocalBranchLeaving;
            }
        }

        void OnLocalBranchLeaving( object sender, EventMonitoredArgs e ) => Delete( e.Monitor );

        void OnLocalBranchEntered( object sender, EventMonitoredArgs e )
        {
            if( _settings.ProduceCKSetupComponents ) EnsureLocalStorePath( e.Monitor );
        }
        public bool EnsureLocalStorePath( IActivityMonitor m )
        {
            return EnsureStorePath( m, _localFeedProvider.GetLocalCKSetupStorePath( m ) );
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
