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

        public bool CheckStatus( IActivityMonitor m, ref GlobalGitStatus gitStatus )
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

                if( gitStatus  == GlobalGitStatus.Unknwon )
                {
                    DumpMixDetail( LogLevel.Error, "Unable to initialize status." );
                    return false;
                }
                DumpMixDetail( LogLevel.Error, $"This is compatible with recorded status '{gitStatus}'." );
                return true;
            }
            else if( current == World.DevelopBranchName )
            {
                if( gitStatus == GlobalGitStatus.Unknwon )
                {
                    gitStatus = GlobalGitStatus.DevelopBranch;
                    m.Info( $"Initializing status on {gitStatus}." );
                    return true;
                }
                if( gitStatus == GlobalGitStatus.DevelopBranch )  return true;
                m.Error( $"All Git folders are on {World.DevelopBranchName}. this is not compatible with status '{gitStatus}'." );
                return false;
            }
            else
            {
                Debug.Assert( current == World.LocalBranchName );
                if( gitStatus == GlobalGitStatus.Unknwon )
                {
                    gitStatus = GlobalGitStatus.LocalBranch;
                    m.Info( $"Initializing status on {gitStatus}." );
                    return true;
                }
                if( gitStatus == GlobalGitStatus.LocalBranch ) return true;
                m.Error( $"All Git folders are on {World.LocalBranchName}. this is not compatible with status '{gitStatus}'." );
                return false;
            }
        }

        public FileSystem FileSystem { get; }

        public IWorldName World { get; }

        public IReadOnlyList<GitFolder> GitFolders => _gitFolders;

    }
}
