using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CK.Core;
using CK.Env.Analysis;
using CK.Text;

namespace CKli
{
    public class XGitStatusAction : XAction
    {
        public XGitStatusAction( Initializer initializer, ActionCollector collector )
            : base( initializer, collector )
        {
        }

        public override bool Run( IActivityMonitor m )
        {
            using( m.OpenInfo( $"Current Git statuses." ) )
            {
                try
                {
                    int gitFoldersCount = 0;
                    List<string> dirty = new List<string>();
                    var gitFolders = NextSiblings.SelectMany( s => s.Descendants<XGitFolder>() ).Select( g => g.GitFolder );
                    foreach( var git in gitFolders )
                    {
                        ++gitFoldersCount;
                        using( m.OpenInfo( $"{git.SubPath} - branch: {git.CurrentBranchName}." ) )
                        {
                            if( git.CheckCleanCommit( m ) ) m.CloseGroup( "Up-to-date." );
                            else
                            {
                                dirty.Add( git.SubPath );
                                m.CloseGroup( "Dirty." );
                            }
                        }
                    }
                    m.CloseGroup( $"{dirty.Count} dirty (out of {gitFoldersCount})." );
                    if( dirty.Count > 0 ) m.Info( $"Dirty: {dirty.Concatenate()}" );
                    var byActiveBranch = gitFolders.GroupBy( g => g.CurrentBranchName );
                    if( byActiveBranch.Count() > 1 )
                    {
                        using( m.OpenInfo( $"{byActiveBranch.Count()} different branches:" ) )
                        {
                            foreach( var b in byActiveBranch )
                            {
                                m.Info( $"Branch '{b.Key}': {b.Select( g => g.SubPath.Path ).Concatenate()}" );
                            }
                        }
                    }
                    else m.Info( $"All {gitFoldersCount} git folders are on '{byActiveBranch.First().Key}' branch." );

                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
            }
        }
    }
}
