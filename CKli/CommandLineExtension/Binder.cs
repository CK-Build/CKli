using Mono.Cecil.Cil;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using static System.CommandLine.Binder;

namespace System.CommandLine
{
    /// <summary>
    /// Offers an easy way to bind handler parameters.
    /// </summary>
    public static class Binder
    {
        sealed class ConsoleBinder : BinderBase<IConsole>
        {
            protected override IConsole GetBoundValue( BindingContext bindingContext ) => bindingContext.Console;
            public static readonly ConsoleBinder Default = new ConsoleBinder();
        }

        sealed class ParserResultBinder : BinderBase<ParseResult>
        {
            protected override ParseResult GetBoundValue( BindingContext bindingContext ) => bindingContext.ParseResult;
            public static readonly ParserResultBinder Default = new ParserResultBinder();
        }

        sealed class RequiredServiceBinder<T> : BinderBase<T> where T : notnull
        {
            protected override T GetBoundValue( BindingContext bindingContext ) => (T?)bindingContext.GetService( typeof( T ) )
                                                                                    ?? throw new Exception( $"Required service '{typeof( T )}' not found in BindingContext." );
        }

        /// <summary>
        /// Binds to the <see cref="BindingContext.Console"/>.
        /// See <see cref="HandlerWithExitCode"/> for why this is required.
        /// </summary>
        public static IValueDescriptor<IConsole> Console => ConsoleBinder.Default;

        /// <summary>
        /// Binds to the <see cref="BindingContext.ParseResult"/>.
        /// </summary>
        public static IValueDescriptor<ParseResult> ParseResult => ParserResultBinder.Default;

        /// <summary>
        /// Binds to the required service from <see cref="BindingContext"/>.
        /// </summary>
        public static IValueDescriptor<T> Service<T>() where T : notnull => new RequiredServiceBinder<T>();

        sealed class ContantDescriptor<T> : BinderBase<T>
        {
            readonly T _value;

            public ContantDescriptor( T value )
            {
                _value = value;
            }

            protected override T GetBoundValue( BindingContext bindingContext ) => _value;
        }

        internal static IValueDescriptor<T> Constant<T>( T value ) => new ContantDescriptor<T>( value );
    }

}

