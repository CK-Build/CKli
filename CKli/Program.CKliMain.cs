using CK.Core;
using CK.Env;
using CK.SimpleKeyVault;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CKli
{


    partial class Program
    {
        static int CkliMain( IActivityMonitor monitor,
                             ICkliApplicationContext appContext,
                             string[] args )
        {
            Debug.Assert( args.Length > 0 );

            var rootCommand = new Command( "CKli", "Multi repository manager." );
            CommandLineBuilder commandLineBuilder = ConfigureRootCommand( monitor, rootCommand, appContext );
            // The stack area clone command is also available as a root command.
            var clone = CreateCloneCommand( monitor, appContext );
            rootCommand.Add( clone );
            rootCommand.Add( CreateWorldArea( monitor, appContext ) );
            rootCommand.Add( CreateStackArea( monitor, appContext, clone ) );

            var parser = commandLineBuilder.Build();
            return parser.Invoke( args );
        }

        static CommandLineBuilder ConfigureRootCommand( IActivityMonitor monitor, Command rootCommand, ICkliApplicationContext appContext )
        {
            var commandLineBuilder = new CommandLineBuilder( rootCommand );

            var verbosity = new Option<string>( new[] { "--verbosity", "-v" }, "Sets the verbosity." );
            verbosity.AddCompletions( "Quiet", "Minimal", "Normal", "Verbose", "Diagnostic", "q", "m", "n", "v", "d" );
            verbosity.SetDefaultValue( "Quiet" );
            rootCommand.AddGlobalOption( verbosity );
            commandLineBuilder.AddMiddleware( ( context, next ) =>
            {
                var consoleMonitor = new ColoredActivityMonitorConsoleClient();
                monitor.Output.RegisterClient( consoleMonitor );
                context.BindingContext.AddService( _ => monitor );
                context.BindingContext.AddService( _ => consoleMonitor );
                consoleMonitor.MinimalFilter = Parse( context.ParseResult.GetValueForOption( verbosity ) );
                return next( context );


                static LogClamper Parse( string? v )
                {
                    if( v == null ) return new LogClamper( LogFilter.Release, true );
                    Debug.Assert( v.Length > 0 );
                    return v[0] switch
                    {
                        'q' or 'Q' => new LogClamper( LogFilter.Release, true ),
                        'm' or 'M' => new LogClamper( LogFilter.Terse, true ),
                        'n' or 'N' => new LogClamper( LogFilter.Monitor, true ),
                        'v' or 'V' => new LogClamper( LogFilter.Verbose, true ),
                        _ => new LogClamper( LogFilter.Debug, true )
                    };
                }
            } );

            var interactive = new Option<bool>( new[] { "--interactive", "-i" },
                                            "Keeps CKli running, subsequent commands can be entered until 'exit' is entered or ^C is pressed." );
            rootCommand.AddGlobalOption( interactive );
            commandLineBuilder.UseInteractiveMode( interactive, services => new InteractiveContext( services.GetRequiredService<IConsole>(),
                                                                                                    services.GetRequiredService<IActivityMonitor>(),
                                                                                                    services.GetRequiredService<ColoredActivityMonitorConsoleClient>(),
                                                                                                    appContext ) );


            commandLineBuilder.UseDefaults();
            return commandLineBuilder;

        }

        static Command CreateCloneCommand( IActivityMonitor monitor, ICkliApplicationContext appContext )
        {
            var clone = new Command( "clone", "Clones a Stack and all its repositories to the local file system." );
            var repository = new Argument<Uri>( "repository", "The stack repository to clone from. Its name ends with '-Stack'." );
            var directory = new Argument<DirectoryInfo>( "directory",
                                                         () => new DirectoryInfo( Environment.CurrentDirectory ),
                                                         "Parent folder of the created stack folder. Defaults to the current directory." )
                                                         .ExistingOnly();
            var isPrivate = new Option<bool>( "--private", "Indicates a private repository." );
            var allowDuplicate = new Option<bool>( "--allowDuplicate", "Allows a repository that already exists in \"stack list\" to be cloned." );
            clone.AddArgument( repository );
            clone.AddArgument( directory );
            clone.AddOption( isPrivate );
            clone.AddOption( allowDuplicate );
            clone.SetHandler(
                ( repository, directory, isPrivate, allowDuplicate, InteractiveContext ) =>
                {
                    var r = StackRoot.Create( monitor,
                                              appContext,
                                              repository,
                                              directory.FullName,
                                              !isPrivate,
                                              allowDuplicate,
                                              openDefaultWorld: InteractiveContext.IsInteractive );
                    if( r == null ) return -1;
                    if( InteractiveContext.IsInteractive )
                    {
                        InteractiveContext.SetCurrentStack( r );
                    }
                    else
                    {
                        r.Dispose();
                    }
                    return 0;
                },
                repository, directory, isPrivate, allowDuplicate, Binder.Service<InteractiveContext>() );
            return clone;
        }

        static Command CreateStackArea( IActivityMonitor monitor, ICkliApplicationContext appContext, Command clone )
        {
            var stackArea = new Command( "stack", "Commands related to Stacks." );
            stackArea.AddCommand( clone );
            stackArea.AddCommand( CreateListCommand( monitor, appContext ) );
            return stackArea;

            static Command CreateListCommand( IActivityMonitor monitor, ICkliApplicationContext appContext )
            {
                var list = new Command( "list", "Lists stacks and their worlds that have been created so far, including duplicated ones." );
                list.SetHandler( console =>
                {
                    var r = StackRootRegistry.Load( monitor, appContext.UserHostPath );
                    var infos = r.GetListInfo().ToList();
                    if( infos.Count == 0 )
                    {
                        console.WriteLine( $"No registered stacks yet." );
                    }
                    else
                    {
                        foreach( var (Primary, _, Duplicates) in infos )
                        {
                            DumpInfo( console, Primary, false );
                        }
                        foreach( var (Primary, _, Duplicates) in infos )
                        {
                            if( Duplicates.Any() )
                            {
                                console.WriteLine( $"Duplicates for '{Primary.StackName}':" );
                                {
                                    foreach( var d in Duplicates )
                                    {
                                        DumpInfo( console, d.Stack, d.BadUrl );
                                    }
                                }
                            }
                        }
                    }
                }, Binder.Console );
                return list;

                static void DumpInfo( IConsole console, StackRootRegistry.StackInfo info, bool badUrl )
                {
                    console.WriteLine( $"[{(info.IsPublic ? "public" : "      ")}] {info.StackName} - {info.RootPath} --> {info.StackUrl}{(badUrl ? " (duplicate repository!)" : "")}" );
                    if( info.WorldDefinitions.Count == 0 )
                    {
                        Console.WriteLine( $"        No World file definition in this stack!" );
                    }
                    else
                    {
                        Console.Write( $"          Worlds: " );
                        bool atLeastOne = false;
                        foreach( var w in info.WorldDefinitions )
                        {
                            var sep = atLeastOne ? ", " : "";
                            atLeastOne = true;
                            if( Directory.Exists( w.Root ) )
                            {
                                Console.Write( $"{sep}{w.FullName}" );
                            }
                            else
                            {
                                Console.Write( $"{sep}{w.FullName} (not cloned)" );
                            }
                        }
                        Console.WriteLine();
                    }
                }
            }
        }

        static Command CreateWorldArea( IActivityMonitor monitor, ICkliApplicationContext appContext )
        {
            var worldArea = new Command( "world", "Commands related to a World. The current directory must be in a World." );
            worldArea.AddCommand( CreateStatusCommand( monitor, appContext ) );
            return worldArea;

            static Command CreateStatusCommand( IActivityMonitor monitor, ICkliApplicationContext appContext )
            {
                var status = new Command( "status", "Displays the status of the World and all its repositories." );
                var gitOnly = new Option<bool>( new[] { "--git", "--gitOnly" }, "Checks only the Git status." );
                status.AddOption( gitOnly );
                status.SetHandler( (console, gitOnly) =>
                {
                    if( !StackRoot.TryLoad( monitor, appContext, Environment.CurrentDirectory, out var stack ) ) 
                    {
                        return -2;
                    }
                    if( stack?.World == null )
                    {
                        monitor.Error( $"Current directory must be inside a World." );
                        return -1;
                    }
                    var status = stack.World.FileSystem.GetSimpleMultipleStatusInfo( monitor, !gitOnly );
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
                }, Binder.Console, gitOnly );
                return status;
            }
        }
    }


}

