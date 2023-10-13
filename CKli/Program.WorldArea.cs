using CK.Core;
using CK.Env;
using System;
using System.CommandLine;
using System.Linq;

namespace CKli
{
    partial class Program
    {
        static Command CreateWorldArea()
        {
            var worldArea = new Command( "world", "Commands related to a World. The current directory must be in a World." );
            worldArea.AddCommand( CreateStatusCommand() );
            return worldArea;

        }

        static Command CreateStatusCommand()
        {
            var status = new Command( "status", "Displays the status of the World and all its repositories." );
            var gitOnly = new Option<bool>( new[] { "--git", "--gitOnly" }, "Checks only the Git status." );
            status.AddOption( gitOnly );
            status.SetHandler( ( console, gitOnly, ckliContext ) =>
            {
                if( !ckliContext.TryGetCurrentWorld( out var world ) )
                { 
                    return -1;
                }
                FileSystem.SimpleMultipleStatusInfo status = world.FileSystem.GetSimpleMultipleStatusInfo( ckliContext.Monitor, !gitOnly );
                if( status.RepositoryStatus.Count == 0 )
                {
                    console.WriteLine( "No valid Git repository." );
                }
                else
                {
                    foreach( var s in status.RepositoryStatus )
                    {
                        var ahead = !s.CommitAhead.HasValue
                                        ? "(no remote branch)"
                                        : s.CommitAhead.Value == 0
                                            ? "(on par with origin)"
                                            : $"({s.CommitAhead.Value} commits ahead origin)";

                        console.WriteLine( $"[{(s.IsDirty ? "Dirty" : "    ")}] - {s.DisplayName} - branch: {s.CurrentBranchName} {ahead}" );
                    }
                    if( status.SingleBranchName != null )
                    {
                        console.Write( $"All repositories are on branch '{status.SingleBranchName}'" );
                        if( status.DirtyCount > 0 ) console.Write( $" ({status.DirtyCount} are dirty)" );
                        console.WriteLine( "." );
                    }
                    else
                    {
                        var branches = status.RepositoryStatus.GroupBy( s => s.CurrentBranchName )
                                                .Select( g => (B: g.Key, C: g.Count(), D: g.Select( x => x.DisplayName.Path )) )
                                                .OrderBy( e => e.C );
                        console.WriteLine( $"Multiple branches are checked out:" );
                        foreach( var b in branches )
                        {
                            console.WriteLine( $"{b.B} ({b.C}) => {b.D.Concatenate()}" );
                        }
                    }
                    if( status.HasPluginInitializationError is true )
                    {
                        console.Write( $"/!\\ Plugin initialization errors for: {status.RepositoryStatus.Where( r => r.PluginCount == null ).Select( r => r.DisplayName.Path ).Concatenate()}." );
                    }
                }
                return 0;
            }, Binder.Console, gitOnly, Binder.RequiredService<ICkliContext>() );
            return status;
        }
    }


}

