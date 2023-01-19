// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.CommandLine.Binding;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace System.CommandLine
{
    /// <summary>
    /// Provides methods for creating command handlers that accept an int returning function
    /// that sets the exit code of the <see cref="InvocationContext"/>.
    /// This fixes https://learn.microsoft.com/en-us/dotnet/standard/commandline/model-binding#set-exit-codes for
    /// synchronous handlers.
    /// </summary>
    public static class HandlerWithExitCode
    {
        sealed class CommandWithExitCodeHandler : ICommandHandler
        {
            private readonly Func<InvocationContext, int>? _syncHandle;

            public CommandWithExitCodeHandler( Func<InvocationContext, int> handle )
                => _syncHandle = handle ?? throw new ArgumentNullException( nameof( handle ) );

            public int Invoke( InvocationContext context )
            {
                if( _syncHandle is not null )
                {
                    return context.ExitCode = _syncHandle( context );
                }
                return 0;
            }

            public Task<int> InvokeAsync( InvocationContext context )
            {
                return Task.FromResult( Invoke( context ) );
            }
        }

        private static T? GetValueForHandlerParameter<T>( IValueDescriptor<T> symbol, InvocationContext context )
        {
            if( symbol is IValueSource valueSource &&
                valueSource.TryGetValue( symbol, context.BindingContext, out var boundValue ) &&
                boundValue is T value )
            {
                return value;
            }
            else
            {
                return symbol switch
                {
                    Argument<T> argument => context.ParseResult.GetValueForArgument( argument ),
                    Option<T> option => context.ParseResult.GetValueForOption( option ),
                    _ => throw new ArgumentException(),
                };
            }
        }

        /// <summary>
        /// Sets a command's handler based on a synchronous function that returns the exit code.
        /// </summary>
        public static void SetHandler(
            this Command command,
            Func<int> handle ) =>
            command.Handler = new CommandWithExitCodeHandler( _ => handle() );

        /// <inheritdoc cref="SetHandler(Command, Func{int})"/>
        public static void SetHandler<T>(
            this Command command,
            Func<T,int> handle,
            IValueDescriptor<T> symbol ) =>
            command.Handler = new CommandWithExitCodeHandler(
                context =>
                {
                    var value1 = GetValueForHandlerParameter( symbol, context );
                    return handle( value1! );
                } );

        /// <inheritdoc cref="SetHandler(Command, Func{int})"/>
        public static void SetHandler<T1, T2>(
            this Command command,
            Func<T1, T2, int> handle,
            IValueDescriptor<T1> symbol1,
            IValueDescriptor<T2> symbol2 ) =>
            command.Handler = new CommandWithExitCodeHandler(
                context =>
                {
                    var value1 = GetValueForHandlerParameter( symbol1, context );
                    var value2 = GetValueForHandlerParameter( symbol2, context );
                    return handle( value1!, value2! );
                } );

        /// <inheritdoc cref="SetHandler(Command, Func{int})"/>
        public static void SetHandler<T1, T2, T3>(
            this Command command,
            Func<T1, T2, T3,int> handle,
            IValueDescriptor<T1> symbol1,
            IValueDescriptor<T2> symbol2,
            IValueDescriptor<T3> symbol3 ) =>
            command.Handler = new CommandWithExitCodeHandler(
                context =>
                {
                    var value1 = GetValueForHandlerParameter( symbol1, context );
                    var value2 = GetValueForHandlerParameter( symbol2, context );
                    var value3 = GetValueForHandlerParameter( symbol3, context );

                    return handle( value1!, value2!, value3! );
                } );

        /// <inheritdoc cref="SetHandler(Command, Func{int})"/>
        public static void SetHandler<T1, T2, T3, T4>(
            this Command command,
            Func<T1, T2, T3, T4,int> handle,
            IValueDescriptor<T1> symbol1,
            IValueDescriptor<T2> symbol2,
            IValueDescriptor<T3> symbol3,
            IValueDescriptor<T4> symbol4 ) =>
            command.Handler = new CommandWithExitCodeHandler(
                context =>
                {
                    var value1 = GetValueForHandlerParameter( symbol1, context );
                    var value2 = GetValueForHandlerParameter( symbol2, context );
                    var value3 = GetValueForHandlerParameter( symbol3, context );
                    var value4 = GetValueForHandlerParameter( symbol4, context );

                    return handle( value1!, value2!, value3!, value4! );
                } );

        /// <inheritdoc cref="SetHandler(Command, Func{int})"/>
        public static void SetHandler<T1, T2, T3, T4, T5>(
            this Command command,
            Func<T1, T2, T3, T4, T5, int> handle,
            IValueDescriptor<T1> symbol1,
            IValueDescriptor<T2> symbol2,
            IValueDescriptor<T3> symbol3,
            IValueDescriptor<T4> symbol4,
            IValueDescriptor<T5> symbol5 ) =>
            command.Handler = new CommandWithExitCodeHandler(
                context =>
                {
                    var value1 = GetValueForHandlerParameter( symbol1, context );
                    var value2 = GetValueForHandlerParameter( symbol2, context );
                    var value3 = GetValueForHandlerParameter( symbol3, context );
                    var value4 = GetValueForHandlerParameter( symbol4, context );
                    var value5 = GetValueForHandlerParameter( symbol5, context );

                    return handle( value1!, value2!, value3!, value4!, value5! );
                } );

        /// <inheritdoc cref="SetHandler(Command, Func{int})"/>
        public static void SetHandler<T1, T2, T3, T4, T5, T6>(
            this Command command,
            Func<T1, T2, T3, T4, T5, T6, int> handle,
            IValueDescriptor<T1> symbol1,
            IValueDescriptor<T2> symbol2,
            IValueDescriptor<T3> symbol3,
            IValueDescriptor<T4> symbol4,
            IValueDescriptor<T5> symbol5,
            IValueDescriptor<T6> symbol6 ) =>
            command.Handler = new CommandWithExitCodeHandler(
                context =>
                {
                    var value1 = GetValueForHandlerParameter( symbol1, context );
                    var value2 = GetValueForHandlerParameter( symbol2, context );
                    var value3 = GetValueForHandlerParameter( symbol3, context );
                    var value4 = GetValueForHandlerParameter( symbol4, context );
                    var value5 = GetValueForHandlerParameter( symbol5, context );
                    var value6 = GetValueForHandlerParameter( symbol6, context );

                    return handle( value1!, value2!, value3!, value4!, value5!, value6! );
                } );

        /// <inheritdoc cref="SetHandler(Command, Func{int})"/>
        public static void SetHandler<T1, T2, T3, T4, T5, T6, T7>(
            this Command command,
            Func<T1, T2, T3, T4, T5, T6, T7, int> handle,
            IValueDescriptor<T1> symbol1,
            IValueDescriptor<T2> symbol2,
            IValueDescriptor<T3> symbol3,
            IValueDescriptor<T4> symbol4,
            IValueDescriptor<T5> symbol5,
            IValueDescriptor<T6> symbol6,
            IValueDescriptor<T7> symbol7 ) =>
            command.Handler = new CommandWithExitCodeHandler(
                context =>
                {
                    var value1 = GetValueForHandlerParameter( symbol1, context );
                    var value2 = GetValueForHandlerParameter( symbol2, context );
                    var value3 = GetValueForHandlerParameter( symbol3, context );
                    var value4 = GetValueForHandlerParameter( symbol4, context );
                    var value5 = GetValueForHandlerParameter( symbol5, context );
                    var value6 = GetValueForHandlerParameter( symbol6, context );
                    var value7 = GetValueForHandlerParameter( symbol7, context );

                    return handle( value1!, value2!, value3!, value4!, value5!, value6!, value7! );
                } );

        /// <inheritdoc cref="SetHandler(Command, Func{int})"/>
        public static void SetHandler<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Command command,
            Func<T1, T2, T3, T4, T5, T6, T7, T8, int> handle,
            IValueDescriptor<T1> symbol1,
            IValueDescriptor<T2> symbol2,
            IValueDescriptor<T3> symbol3,
            IValueDescriptor<T4> symbol4,
            IValueDescriptor<T5> symbol5,
            IValueDescriptor<T6> symbol6,
            IValueDescriptor<T7> symbol7,
            IValueDescriptor<T8> symbol8 ) =>
            command.Handler = new CommandWithExitCodeHandler(
                context =>
                {
                    var value1 = GetValueForHandlerParameter( symbol1, context );
                    var value2 = GetValueForHandlerParameter( symbol2, context );
                    var value3 = GetValueForHandlerParameter( symbol3, context );
                    var value4 = GetValueForHandlerParameter( symbol4, context );
                    var value5 = GetValueForHandlerParameter( symbol5, context );
                    var value6 = GetValueForHandlerParameter( symbol6, context );
                    var value7 = GetValueForHandlerParameter( symbol7, context );
                    var value8 = GetValueForHandlerParameter( symbol8, context );

                    return handle( value1!, value2!, value3!, value4!, value5!, value6!, value7!, value8! );
                } );
    }
}
