using CK.Core;
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

        public CKSetupStoreTestHelperConfigFile( GitFolder f, ISolutionSettings settings, ILocalFeedProvider localFeedProvider )
            : base( f, "RemoteStore.TestHelper.config" )
        {
            _settings = settings;
            _localFeedProvider = localFeedProvider;
            if( settings.ProduceCKSetupComponents )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
        }

        public void Dispose()
        {
            if( _settings.ProduceCKSetupComponents )
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
