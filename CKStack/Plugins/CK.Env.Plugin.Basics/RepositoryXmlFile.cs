using CK.Core;
using CK.Env.MSBuildSln;
using SimpleGitVersion;
using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class RepositoryXmlFile : XmlFilePluginBase, IDisposable, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionDriver _driver;
        readonly ChildObject<RepositoryInfoOptions> _simpleGitVersionOption;

        public RepositoryXmlFile( GitRepository f, NormalizedPath branchPath, SolutionDriver driver )
            : base( f, branchPath, branchPath.AppendPart( "RepositoryInfo.xml" ), XNamespace.None + "RepositoryInfo" )
        {
            if( StandardPluginBranch == StandardGitStatus.Local )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
            _driver = driver;
            _driver.OnStartBuild += OnStartBuild;
            _driver.OnEndBuild += OnEndBuild;

            _simpleGitVersionOption = new ChildObject<RepositoryInfoOptions>( this,
                                                                              XNamespace.None + "SimpleGitVersion",
                                                                              e => new RepositoryInfoOptions( e ),
                                                                              o => o.ToXml() );

        }

        void OnStartBuild( object? sender, BuildStartEventArgs e )
        {
            var o = _simpleGitVersionOption.EnsureObject();
            if( !o.IgnoreDirtyWorkingFolder )
            {
                e.Memory.Add( this, this );
                o.IgnoreDirtyWorkingFolder = true;
                _simpleGitVersionOption.UpdateXml( e.Monitor, true );
            }
        }

        void OnEndBuild( object? sender, BuildEndEventArgs e )
        {
            // We must always reset the in-memory option if we have changed it.
            if( e.BuildStartArgs.Memory.ContainsKey( this ) )
            {
                _simpleGitVersionOption.EnsureObject().IgnoreDirtyWorkingFolder = false;
                // If the build is protected by a Git reset, no need to update the file.
                if( !e.BuildStartArgs.IsUsingDirtyFolder )
                {
                    _simpleGitVersionOption.UpdateXml( e.Monitor, true );

                }
            }
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        void OnLocalBranchEntered( object? sender, EventMonitoredArgs e )
        {
            EnsureLocalBranch( e.Monitor );
            _simpleGitVersionOption.UpdateXml( e.Monitor, true );
        }

        void OnLocalBranchLeaving( object? sender, EventMonitoredArgs e )
        {
            RemoveLocalBranch( e.Monitor );
            _simpleGitVersionOption.UpdateXml( e.Monitor, true );
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
        public void ApplySettings( IActivityMonitor monitor )
        {
            if( !this.CheckCurrentBranch( monitor ) ) return;

            var solution = _driver.GetSolution( monitor, allowInvalidSolution: true );
            if( solution != null )
            {
                solution.Tag<SolutionFile>()?.EnsureSolutionItemFile( monitor, FilePath.RemovePrefix( BranchPath ) );
            }

            var o = _simpleGitVersionOption.EnsureObject();
            o.IgnoreDirtyWorkingFolder = false;
            EnsureBranchMapping( monitor, GitFolder.World.DevelopBranchName, CIBranchVersionMode.LastReleaseBased, "develop" );
            if( StandardPluginBranch == StandardGitStatus.Local )
            {
                EnsureLocalBranch( monitor );
            }
            else
            {
                RemoveLocalBranch( monitor );
            }
            _simpleGitVersionOption.UpdateXml( monitor, false );
            Save( monitor );
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
            var o = _simpleGitVersionOption.EnsureObject();
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
            var o = _simpleGitVersionOption.EnsureObject();
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
