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
                repository, directory, isPrivate, allowDuplicate, Binder.RequiredService<InteractiveContext>() );
            return clone;
        }
    }


}

