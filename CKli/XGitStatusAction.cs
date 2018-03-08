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
                    List<string> dirty = new List<string>();
                    var gitFolders = NextSiblings.SelectMany( s => s.Descendants<XGitFolder>() ).Select( g => g.GitFolder );
                    foreach( var git in gitFolders )
                    {
                        using( m.OpenInfo( $"{git.SubPath} - branch: {git.CurrentBranchName}." ) )
                        {
                            var s = git.GetDirtyDescription( true );
                            if( s == null ) m.CloseGroup( "Up-to-date." );
                            else
                            {
                                m.Info( s );
                                dirty.Add( git.SubPath );
                                m.CloseGroup( "Dirty." );
                            }
                        }
                    }
                    m.CloseGroup( $"{dirty.Count} dirty." );
                    if( dirty.Count > 0 ) m.Info( $"Dirty: {dirty.Concatenate()}" );
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
