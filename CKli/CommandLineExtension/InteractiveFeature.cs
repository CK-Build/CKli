using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace System.CommandLine
{
    public static class InteractiveFeature
    {
        sealed class MiddlewareContext<T> : IInvocationResult where T : InteractiveService
        {
            readonly Func<IServiceProvider,T> _serviceFactory;
            readonly Option<bool> _interactiveOption;
            [AllowNull] T _service;
            bool? _isInteractive;

            public MiddlewareContext( Func<IServiceProvider, T> service, Option<bool> interactiveOption )
            {
                _serviceFactory = service;
                _interactiveOption = interactiveOption;
            }

            // This Middleware is used as Nop invocation result.
            void IInvocationResult.Apply( InvocationContext context )
            {
            }

            public async Task Middleware( InvocationContext context, Func<InvocationContext, Task> next )
            {
                if( _isInteractive is null )
                {
                    AddInteractionService( context );
                    _isInteractive = context.ParseResult.GetValueForOption( _interactiveOption );
                    if( _isInteractive.Value )
                    {
                        if( _service.EnterInteractiveMode( context ) )
                        {
                            // Execute the first command in the calling context if
                            // this is not the root command.
                            if( context.ParseResult.CommandResult.Parent != null )
                            {
                                await next( context );
                            }
                            else
                            {
                                // This forgets any ParseError handling (Required command was not provided.):
                                // Entering the interactive mode can be done with --interactive option only.
                                context.InvocationResult = this;
                            }
                            // If the interactive service implements the IConsole, use it.
                            var c = _service as IConsole ?? context.Console;
                            context.ExitCode = await RunLoop( c );
                            return;
                        }
                    }
                }
                await next( context );
            }

            async Task<int> RunLoop( IConsole console )
            {
                string? input;
                while( _service.IsInteractive && (input = _service.ReadLine()) != null )
                {
                    input = input.Trim();
                    if( input.Length > 0 || (input = _service.OnEmptyInputLine()) != null )
                    {
                        var parseResult = _service.CurrentParser.Parse( CommandLineStringSplitter.Instance.Split( input ).ToArray(), input );
                        var exitCode = await parseResult.InvokeAsync( console );
                        _service.OnCommandExecuted( parseResult, exitCode );
                    }
                }
                return 0;
            }

            void AddInteractionService( InvocationContext context )
            {
                context.BindingContext.AddService<InteractiveService>( _serviceFactory );
                _service = (T)context.BindingContext.GetService( typeof( InteractiveService ) )!;
                var actualT = typeof( T );
                if( actualT != typeof( InteractiveService ) )
                {
                    context.BindingContext.AddService( actualT, _ => _service );
                }
            }

        }

        /// <summary>
        /// Enables the interactive mode.
        /// </summary>
        /// <param name="builder">A command line builder.</param>
        /// <param name="interactiveOption">The boolean option that triggers the feature.</param>
        /// <param name="interactiveService">The interactive service factory.</param>
        /// <returns>The same instance of <see cref="CommandLineBuilder"/>.</returns>
        public static CommandLineBuilder UseInteractiveMode<T>( this CommandLineBuilder builder,
                                                                Option<bool> interactiveOption,
                                                                Func<IServiceProvider,T> interactiveService ) where T : InteractiveService
                                        
        {
            builder.AddMiddleware( new MiddlewareContext<T>( interactiveService, interactiveOption ).Middleware );
            return builder;
        }
    }


}

