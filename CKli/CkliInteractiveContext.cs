using CK.Core;
using CK.Env;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli
{
    sealed partial class CkliInteractiveContext : InteractiveService, ICkliContext
    {
        StackRootRegistry? _stackRegistry;
        readonly IConsole _console;
        readonly IActivityMonitor _monitor;
        readonly ColoredActivityMonitorConsoleClient _coloredConsole;
        readonly ICkliApplicationContext _appContext;
        StackRoot? _currentStack;

        public CkliInteractiveContext( IConsole console,
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
        /// <see cref="Is"/>
        /// </summary>
        /// <param name="newOne">The new current stack. Null to close it.</param>
        public void SetCurrentStack( StackRoot? newOne )
        {
            Throw.CheckState( IsInteractive );
            if( _currentStack != null )
            {
                _currentStack.CloseWorld( _monitor );
                _currentStack.Dispose();
            }
            _currentStack = newOne;
        }

        /// <inheritdoc />
        public bool TryGetCurrentWorld( [NotNullWhen( true )] out World? world )
        {
            StackRoot? stack = _currentStack;
            if( stack == null
                && !StackRoot.TryLoad( _monitor, _appContext, Environment.CurrentDirectory, out stack ) )
            {
                world = null;
                return false;
            }
            if( (world = stack?.World) == null )
            {
                _monitor.Error( $"Current directory must be inside a World." );
                return false;
            }
            return true;
        }


        /// <summary>
        /// Clears the screen.
        /// </summary>
        public void ClearScreen() => System.Console.Clear();

        /// <summary>
        /// Gets the <see cref="ColoredActivityMonitorConsoleClient"/>.
        /// </summary>
        public ColoredActivityMonitorConsoleClient ColoredConsole => _coloredConsole;

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

        protected internal override string? OnEmptyInputLine() => "ls";
    }
}

