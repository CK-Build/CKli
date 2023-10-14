using CK.Core;
using CK.Env;
using LibGit2Sharp;
using System;
using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CKli
{
    partial class Program
    {
        static Command CreateWorldArea()
        {
            var worldArea = new Command( "world", "Commands related to a World. The current directory must be in a World." );
            worldArea.AddCommand( CreateStatusCommand() );
            worldArea.AddCommand( CreateCheckoutCommand() );
            worldArea.AddCommand( CreateFetchCommand() );
            worldArea.AddCommand( CreatePullCommand() );
            return worldArea;
        }

        static Command CreateStatusCommand()
        {
            var status = new Command( "status", "Displays the status of the World and all its repositories." );
            // The status command should also be able to return statuses from the future plugins.
            // The option is useless for the moment.
            var gitOnly = new Option<bool>( new[] { "--git", "--gitOnly" }, "Checks only the Git status." );
            status.AddOption( gitOnly );
            status.SetHandler( ( console, gitOnly, ckliContext ) =>
            {
                if( !ckliContext.TryGetCurrentWorld( out var world ) )
                { 
                    return -1;
                }
                FileSystem.SimpleMultipleStatusInfo status = world.FileSystem.GetSimpleMultipleStatusInfo();
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
                }
                return 0;
            }, Binder.Console, gitOnly, Binder.RequiredService<ICkliContext>() );
            return status;
        }

        static Command CreateCheckoutCommand()
        {
            var co = new Command( "checkout", "Checkouts the specified branch. All the repositories must be clean." );
            co.AddAlias( "co" );

            var target = new Argument<string>( "branch", "Branch name to checkout." );
            var from = new Option<string>( "-from", "Starting branch name from which the branch will be created if it doesn't exist yet." );
            co.AddArgument( target );
            co.AddOption( from );

            co.SetHandler( ( branchName, startingBranchName, ckliContext ) =>
            {
                if( !ckliContext.TryGetCurrentWorld( out var world ) )
                {
                    return -1;
                }
                Debug.Assert( "-local".Length == 6 );
                if( startingBranchName == null && branchName.Length > 6 && branchName.EndsWith( "-local", StringComparison.Ordinal ) )
                {
                    startingBranchName = branchName.Substring( 0, branchName.Length - 6 );
                }
                bool success = world.Checkout( ckliContext.Monitor, branchName, startingBranchName );
                return success ? 0 : -1;
            }, target, from, Binder.RequiredService<ICkliContext>() );
            return co;
        }

        static Command CreateFetchCommand()
        {
            var fetch = new Command( "fetch", "Fetches 'origin' (or all remotes) branches into this repository." );
            var all = new Option<bool>(
                new[] { "--all", "-a" },
                "Fetches branches from all remotes (including the 'origin' one)." );
            fetch.AddOption( all );

            fetch.SetHandler( ( all, ckliContext ) =>
            {
                if( !ckliContext.TryGetCurrentWorld( out var world ) )
                {
                    return -1;
                }
                bool success = world.Fetch( ckliContext.Monitor, !all );
                return success ? 0 : -1;
            }, all, Binder.RequiredService<ICkliContext>() );
            return fetch;
        }

        static Command CreatePullCommand()
        {
            var pull = new Command( "pull", "Pulls the specified branch. All the repositories must be clean." );
            var ff = new Option<bool>(
                new[] { "--fast-forward", "-ff" },
                "When possible resolve the merge as a fast-forward (only update the branch pointer to match the " +
                "merged branch instead of creating a merge commit)." );
            var strategy = new Option<string>(
                new[] { "--strategy", "-s" },
                "Merge strategy to apply. By default (none), any conflict cancels the operation. " +
                "By choosing 'ours' or 'theirs', more conflicts can be resolved." );
            strategy.AddCompletions( "none", "ours", "theirs" );
            pull.AddOption( ff );
            pull.AddOption( strategy );

            pull.SetHandler( ( ff, strategy, ckliContext ) =>
            {
                if( !ckliContext.TryGetCurrentWorld( out var world ) )
                {
                    return -1;
                }
                var s = strategy switch { "ours" => MergeFileFavor.Ours, "theirs" => MergeFileFavor.Theirs, _ => MergeFileFavor.Normal };
                bool success = world.Pull( ckliContext.Monitor, s, ff );
                return success ? 0 : -1;
            }, ff, strategy, Binder.RequiredService<ICkliContext>() );
            return pull;
        }


    }


}

