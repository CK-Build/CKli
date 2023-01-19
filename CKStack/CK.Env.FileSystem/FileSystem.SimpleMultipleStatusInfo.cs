using CK.Core;
using System.Collections.Generic;
using System.Threading;
using static CK.Env.GitRepositoryBase;

namespace CK.Env
{
    public partial class FileSystem
    {
        public sealed class SimpleMultipleStatusInfo
        {
            public SimpleMultipleStatusInfo( IReadOnlyList<SimpleStatusInfo> repositoryStatus,
                                             string? singleBranch,
                                             int dirty,
                                             bool? pluginError )
            {
                RepositoryStatus = repositoryStatus;
                SingleBranchName = singleBranch;
                DirtyCount = dirty;
                HasPluginInitializationError = pluginError;
            }

            /// <summary>
            /// Gets the simple repositories status.
            /// </summary>
            public IReadOnlyList<SimpleStatusInfo> RepositoryStatus { get; }

            /// <summary>
            /// Gets whether the repositories are all on the same branch.
            /// </summary>
            public string? SingleBranchName { get; }

            /// <summary>
            /// Gets the number of repositories that are dirty.
            /// </summary>
            public int DirtyCount { get; }

            /// <summary>
            /// Gets whether at least one failed to initialize properly on one branch.
            /// Null if it's not relevant since plugins are not initialized.
            /// </summary>
            public bool? HasPluginInitializationError { get; }
        }

        /// <summary>
        /// Gets a <see cref="SimpleMultipleStatusInfo"/> for the current <see cref="GitFolders"/>.
        /// Use <see cref="LoadAllGitFolders(IActivityMonitor, out bool)"/> to first load all the declared folders.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="initializePlugins">True to ensure that the plugins are initialized.</param>
        /// <returns>The multi status.</returns>
        public SimpleMultipleStatusInfo GetSimpleMultipleStatusInfo( IActivityMonitor monitor, bool initializePlugins )
        {
            var status = new SimpleStatusInfo[_gits.Count];
            bool isSingleBranch = true;
            bool hasPluginInitializationError = false;
            int dirtyCount = 0;
            string? b = null;
            for( int i = 0; i < _gits.Count; ++i )
            {
                var g = _gits[i];
                var e = g.GetSimpleStatusInfo( monitor, initializePlugins );
                status[i] = e;
                if( b == null ) b = e.CurrentBranchName;
                else isSingleBranch &= b == e.CurrentBranchName;
                if( e.IsDirty ) ++dirtyCount;
                hasPluginInitializationError |= e.PluginCount is null;
            }
            return new SimpleMultipleStatusInfo( status,
                                                 isSingleBranch && _gits.Count > 0 ? status[0].CurrentBranchName : null,
                                                 dirtyCount,
                                                 hasPluginInitializationError ? (initializePlugins ? true : null) : false );
        }


    }
}
