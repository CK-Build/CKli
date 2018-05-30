using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Captures an immuatble set of <see cref="GitFolder"/> and a <see cref="IWorldName"/>. 
    /// </summary>
    public class GlobalGitContext
    {
        readonly IReadOnlyList<GitFolder> _gitFolders;

        public GlobalGitContext( IWorldName world, IEnumerable<GitFolder> gitFolders )
        {
            if( world == null ) throw new ArgumentNullException( nameof( world ) );
            if( gitFolders == null ) throw new ArgumentNullException( nameof( gitFolders ) );
            World = world;
            _gitFolders = gitFolders.ToList();
            if( _gitFolders.Count == 0 || _gitFolders.Any( g => g.FileSystem != _gitFolders[0].FileSystem) )
            {
                throw new ArgumentException( "At least one GitFolder must be provided, all of them belonging to the same FileSystem.", nameof(gitFolders) );
            }
            FileSystem = _gitFolders[0].FileSystem;
        }

        /// <summary>
        /// Checks whether a <see cref="GlobalGitStatus"/> is compatible with the current state
        /// of the repositories.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="gitStatus">The status to check.</param>
        /// <param name="isTransitioning">Whether we are transitioning between 'develop' and 'develop-local' branches.</param>
        /// <returns>True if the status is valid according to the state of the actual repositories.</returns>
        public bool CheckStatus( IActivityMonitor m, ref GlobalGitStatus gitStatus, bool isTransitioning )
        {
            var bBadBranches = _gitFolders
                                .Where( g => g.CurrentBranchName != World.DevelopBranchName
                                             && g.CurrentBranchName != World.LocalBranchName );
            if( bBadBranches.Any() )
            {
                using( m.OpenError( $"All git folders must be on '{World.DevelopBranchName}' or '{World.LocalBranchName}'." ) )
                {
                    foreach( var b in bBadBranches.GroupBy( g => g.CurrentBranchName ) )
                    {
                        m.Info( $"On branch '{b.Key}': {b.Select( g => g.SubPath.ToString() ).Concatenate()}" );
                    }
                }
                return false;
            }
            var byActiveBranch = _gitFolders.GroupBy( g => g.CurrentBranchName );
            string current = byActiveBranch.SingleOrDefault().Key;
            if( current == null )
            {
                void DumpMixDetail( LogLevel level, string conclusion )
                {
                    using( m.OpenGroup( level, $"Mix of {World.LocalBranchName} and {World.DevelopBranchName}." ) )
                    {
                        foreach( var b in byActiveBranch )
                        {
                            m.Info( $"On branch '{b.Key}': {b.Select( g => g.SubPath.ToString() ).Concatenate()}" );
                        }
                        m.CloseGroup( conclusion );
                    }
                }

                if( gitStatus == GlobalGitStatus.Unknwon )
                {
                    DumpMixDetail( LogLevel.Error, "Unable to initialize status." );
                    return false;
                }
                DumpMixDetail( LogLevel.Error, $"This is compatible with recorded status '{gitStatus}'." );
                return true;
            }
            m.Info( $"All Git folders are on {current}." );
            if( current == World.DevelopBranchName )
            {
                if( !isTransitioning && gitStatus == GlobalGitStatus.LocalBranch )
                {
                    m.Error( $"All Git folders are on {World.DevelopBranchName}. Status is '{gitStatus}'." );
                    return false;
                }
                if( gitStatus == GlobalGitStatus.Unknwon )
                {
                    gitStatus = GlobalGitStatus.DevelopBranch;
                    m.Info( $"Initializing status on {gitStatus}." );
                }
                return true;
            }
            Debug.Assert( current == World.LocalBranchName );
            if( !isTransitioning && gitStatus == GlobalGitStatus.DevelopBranch )
            {
                m.Error( $"All Git folders are on {World.LocalBranchName}. Status is '{gitStatus}'." );
                return false;
            }
            if( gitStatus == GlobalGitStatus.Unknwon )
            {
                gitStatus = GlobalGitStatus.LocalBranch;
                m.Info( $"Initializing status on {gitStatus}." );
            }
            return true;
        }

        /// <summary>
        /// Gets the file system.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gest immutable the world.
        /// </summary>
        public IWorldName World { get; }

        /// <summary>
        /// Gets all the Git folders.
        /// </summary>
        public IReadOnlyList<GitFolder> GitFolders => _gitFolders;

    }
}
