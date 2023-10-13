using CK.Core;
using CK.Env;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli
{
    sealed partial class InteractiveContext : InteractiveService
    {
        StackRootRegistry? _stackRegistry;
        readonly IConsole _console;
        readonly IActivityMonitor _monitor;
        readonly ColoredActivityMonitorConsoleClient _coloredConsole;
        readonly ICkliApplicationContext _appContext;
        StackRoot? _currentStack;

        public InteractiveContext( IConsole console,
                                   IActivityMonitor monitor,
                                   ColoredActivityMonitorConsoleClient coloredConsole,
                                   ICkliApplicationContext appContext )
        {
            _console = console;
            _monitor = monitor;
            _coloredConsole = coloredConsole;
            _appContext = appContext;
        }

        /// <summary>
        /// Gets the basic console.
        /// </summary>
        public IConsole Console => _console;

        /// <summary>
        /// Gets the activity monitor for this context.
        /// </summary>
        public IActivityMonitor Monitor => _monitor;

        /// <summary>
        /// Gets the <see cref="ColoredActivityMonitorConsoleClient"/>.
        /// </summary>
        public ColoredActivityMonitorConsoleClient ColoredConsole => _coloredConsole;

        /// <summary>
        /// Gets the application context.
        /// </summary>
        public ICkliApplicationContext AppContext => _appContext;

        /// <summary>
        /// Gets a reusable stack registry.
        /// </summary>
        public StackRootRegistry GetStackRegistry( bool refresh = false )
        {
            if( _stackRegistry == null || refresh )
            {
                _stackRegistry = StackRootRegistry.Load( Monitor, _appContext.UserHostPath );
            }
            return _stackRegistry;
        }

        /// <summary>
        /// Gets the current stack.
        /// </summary>
        public StackRoot? CurrentStack => _currentStack;

        /// <summary>
        /// Closes the current stack and replaces it with the new one.
        /// </summary>
        /// <param name="newOne">The new current stack.</param>
        [MemberNotNull(nameof(CurrentStack))]
        public void SetCurrentStack( StackRoot? newOne )
        {
            if( _currentStack != null )
            {
                _currentStack.CloseWorld( _monitor );
                _currentStack.Dispose();
            }
            _currentStack = newOne;
        }

        /// <summary>
        /// Clears the screen.
        /// </summary>
        public void ClearScreen() => System.Console.Clear();

        public override string? ReadLine()
        {
            if( CurrentStack == null ) Console.Write( $"CKli> " );
            else
            {
                if( CurrentStack.World == null )
                {
                    Console.Write( $"CKli/{CurrentStack.StackName} > " );
                }
                else
                {
                    Console.Write( $"CKli/{CurrentStack.StackName}/{CurrentStack.World.WorldName}> " );
                }
            }
            return base.ReadLine();
        }

        protected override bool OnEnterInteractiveMode( InvocationContext context )
        {
            var root = context.ParseResult.CommandResult.Command;
            root.Add( CreateSimpleInteractiveCommand( "cls", "Clears the screen.", i => i.ClearScreen() ) );
            root.Add( CreateSimpleInteractiveCommand( "exit", "Leaves the application.", i => i.Exit() ) );
            root.Add( CreateListDirectoryContent() );
            root.Add( CreateChangeDirectoryUp() );
            root.Add( CreateChangeDirectory() );

            // builder.UseVersionOption() cannot be used (duplicate option registration error).
            // We may implement a middleware (but the VersionOption is internal) so we hide
            // the version option here.
            var versionOption = root.Options.FirstOrDefault( o => o.Name == "version" );
            if( versionOption != null ) versionOption.IsHidden = true;
            // And as a workaround, we display the version here.
            Console.WriteLine( $"Entering interactive mode (version: {CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() )})" );

            var builder = new CommandLineBuilder( root );
            builder.AddMiddleware( ( newContext, next ) =>
            {
                newContext.BindingContext.AddService( _ => _monitor );
                newContext.BindingContext.AddService( _ => _coloredConsole );
                newContext.BindingContext.AddService( _ => _appContext );
                newContext.BindingContext.AddService<InteractiveService>( _ => this );
                newContext.BindingContext.AddService( _ => this );
                return next( newContext );
            } );

            builder.UseHelp()
                   .UseTypoCorrections()
                   .UseParseErrorReporting()
                   .UseExceptionHandler()
                   .CancelOnProcessTermination();
            PushParser( builder.Build() );
            return true;

            static Command CreateSimpleInteractiveCommand( string name, string description, Action<InteractiveContext> handler )
            {
                var c = new Command( name, description );
                c.SetHandler( handler, Binder.RequiredService<InteractiveContext>() );
                return c;
            }
        }

        protected internal override string? OnEmptyInputLine() => "ls";
    }
}

