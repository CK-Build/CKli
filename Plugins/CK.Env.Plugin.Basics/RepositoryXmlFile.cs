using CK.Core;
using CK.Text;
using SimpleGitVersion;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class RepositoryXmlFile : XmlFilePluginBase, IDisposable, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionDriver _driver;
        readonly ChildObject<RepositoryInfoOptions> _options;

        public RepositoryXmlFile( GitFolder f, NormalizedPath branchPath, SolutionDriver driver )
            : base( f, branchPath, branchPath.AppendPart( "RepositoryInfo.xml" ), XNamespace.None + "RepositoryInfo" )
        {
            if( PluginBranch == StandardGitStatus.Local )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
            _driver = driver;
            _driver.OnStartBuild += OnStartBuild;
            _driver.OnEndBuild += OnEndBuild;
            _options = new ChildObject<RepositoryInfoOptions>( this, XNamespace.None + "SimpleGitVersion", e => new RepositoryInfoOptions( e ), o => o.ToXml() );
        }

        void OnStartBuild( object sender, BuildStartEventArgs e )
        {
            var o = _options.EnsureObject();
            if( !o.IgnoreDirtyWorkingFolder )
            {
                e.Memory.Add( this, this );
                o.IgnoreDirtyWorkingFolder = true;
                _options.UpdateXml( e.Monitor, true );
            }
        }

        void OnEndBuild( object sender, BuildEndEventArgs e )
        {
            if( !e.BuildStartArgs.IsUsingDirtyFolder
                && e.BuildStartArgs.Memory.ContainsKey( this ) )
            {
                _options.EnsureObject().IgnoreDirtyWorkingFolder = false;
                _options.UpdateXml( e.Monitor, true );
            }
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        void OnLocalBranchEntered( object sender, EventMonitoredArgs e )
        {
            EnsureLocalBranch( e.Monitor );
            _options.UpdateXml( e.Monitor, true );
        }

        void OnLocalBranchLeaving( object sender, EventMonitoredArgs e )
        {
            RemoveLocalBranch( e.Monitor );
            _options.UpdateXml( e.Monitor, true );
        }

        void IDisposable.Dispose()
        {
            GitFolder.OnLocalBranchEntered -= OnLocalBranchEntered;
            GitFolder.OnLocalBranchLeaving -= OnLocalBranchLeaving;
            _driver.OnStartBuild -= OnStartBuild;
            _driver.OnEndBuild -= OnEndBuild;
        }

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            var o = _options.EnsureObject();
            if( o.XmlMigrationRequired )
            {
                Document.Root.RemoveAllNamespaces();
                Document.Root.Elements( SGVSchema.Branches ).Remove();
                Document.Root.Elements( SGVSchema.IgnoreModifiedFiles ).Remove();
                Document.Root.Elements( SGVSchema.Debug ).Remove();
            }
            o.IgnoreDirtyWorkingFolder = false;
            EnsureBranchMapping( m, GitFolder.World.DevelopBranchName, CIBranchVersionMode.LastReleaseBased, "develop" );
            if( PluginBranch == StandardGitStatus.Local )
            {
                EnsureLocalBranch( m );
            }
            else
            {
                RemoveLocalBranch( m );
            }
            _options.UpdateXml( m, false );
            Save( m );
        }

        /// <summary>
        /// Ensures a branch mapping exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The actual branch name (ex: "develop-local").</param>
        /// <param name="versionName">The package suffix (ex: "local").</param>
        /// <param name="mode">The CI mode to use for this branch.</param>
        void EnsureBranchMapping( IActivityMonitor m, string branchName, CIBranchVersionMode mode, string versionName )
        {
            var o = _options.EnsureObject();
            var b = o.Branches.FirstOrDefault( x => x.Name == branchName );
            if( b == null ) o.Branches.Add( b = new RepositoryInfoOptionsBranch( branchName, mode, versionName ) );
            else
            {
                b.CIVersionMode = mode;
                b.VersionName = versionName;
            }
        }

        /// <summary>
        /// Removes the branch mapping if it exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The actual branch name (ex: "develop-local").</param>
        void RemoveBranchMapping( IActivityMonitor m, string branchName )
        {
            var o = _options.EnsureObject();
            int idx = o.Branches.IndexOf( b => b.Name == branchName );
            if( idx >= 0 )
            {
                o.Branches.RemoveAt( idx );
            }
        }

        /// <summary>
        /// Ensures that the document and the local branch mapping exists:
        /// <see cref="IWorldName.LocalBranchName"/> is mapped to "local".
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        void EnsureLocalBranch( IActivityMonitor m ) => EnsureBranchMapping( m, GitFolder.World.LocalBranchName, CIBranchVersionMode.ZeroTimed, "local" );

        /// <summary>
        /// Removes the <see cref="IWorldName.LocalBranchName"/> branch mapping if it exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        void RemoveLocalBranch( IActivityMonitor m ) => RemoveBranchMapping( m, GitFolder.World.LocalBranchName );

    }
}
